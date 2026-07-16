using System.Net;
using System.Net.Sockets;
using KartRider.P236.Server;

namespace KartRider;

internal static class RouterListener
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<SessionGroup> Sessions = [];
    private static readonly Dictionary<uint, LegacySessionProfile> ProfilesByUserNo = [];
    private static readonly Dictionary<string, LegacySessionProfile> ProfilesByUsername =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<uint, LegacySessionProfile> LoginProfiles = [];
    private static readonly Dictionary<uint, IPEndPoint> ObservedUdpEndPoints = [];
    private static TcpListener? _listener;
    private static LegacyUdpServer? _udpServer;
    private static CancellationTokenSource? _stop;
    private static Task? _acceptTask;
    private static ILegacyProfileStore? _profileStore;
    private static P236LicenseProgress.FloorSnapshot? _licenseProgressFloor;
    private static uint _nextSessionOrdinal;

    public static bool IsRunning { get { lock (SyncRoot) return _listener is not null; } }
    public static int TcpPort { get; private set; } = 39312;
    public static int UdpPort { get; private set; } = 39312;
    public static IPEndPoint? CurrentUDPServer { get; private set; }

    public static void Start(P236ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (SyncRoot)
        {
            if (_listener is not null) throw new InvalidOperationException("The P236 listener is already running.");
            P236LicenseProgress.FloorSnapshot licenseProgressFloor =
                P236LicenseProgress.FloorSnapshot.Capture(options);
            Directory.CreateDirectory(options.DataDirectory);
            Directory.CreateDirectory(options.LogDirectory);
            ProfilesByUserNo.Clear();
            ProfilesByUsername.Clear();
            LoginProfiles.Clear();
            ObservedUdpEndPoints.Clear();
            Sessions.Clear();
            _nextSessionOrdinal = 0;
            LoadProfilesLocked(Path.Combine(options.DataDirectory, "profiles.json"), licenseProgressFloor);
            LegacyObserverPolicy.Reload();

            TcpListener listener = new(options.BindAddress, options.TcpPort);
            LegacyUdpServer udpServer = new(options.BindAddress, options.UdpPort);
            CancellationTokenSource stop = new();
            try
            {
                listener.Start();
                udpServer.Start();
                TcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                UdpPort = udpServer.LocalPort;
                CurrentUDPServer = new IPEndPoint(options.AdvertisedAddress, UdpPort);
                _listener = listener;
                _udpServer = udpServer;
                _stop = stop;
                _acceptTask = Task.Run(() => AcceptLoopAsync(listener, stop.Token));
                _licenseProgressFloor = licenseProgressFloor;
            }
            catch
            {
                stop.Cancel();
                stop.Dispose();
                udpServer.Dispose();
                listener.Stop();
                _profileStore = null;
                _licenseProgressFloor = null;
                throw;
            }
        }
        LegacyPacketTrace.LogEvent($"[P236] TCP {options.BindAddress}:{TcpPort}, UDP {options.BindAddress}:{UdpPort}.");
    }

    public static void Stop()
    {
        SessionGroup[] sessions;
        TcpListener? listener;
        LegacyUdpServer? udpServer;
        CancellationTokenSource? stop;
        Task? acceptTask;
        lock (SyncRoot)
        {
            listener = _listener;
            if (listener is null) return;
            _listener = null;
            udpServer = _udpServer;
            _udpServer = null;
            stop = _stop;
            _stop = null;
            acceptTask = _acceptTask;
            _acceptTask = null;
            SaveAllProfilesLocked();
            sessions = [.. Sessions];
            Sessions.Clear();
            LoginProfiles.Clear();
            ObservedUdpEndPoints.Clear();
            ProfilesByUserNo.Clear();
            ProfilesByUsername.Clear();
            _profileStore = null;
            _licenseProgressFloor = null;
            CurrentUDPServer = null;
        }

        try
        {
            TryShutdownStep(() => stop?.Cancel(), "cancelling the listener");
            TryShutdownStep(listener.Stop, "stopping the TCP listener");
            TryShutdownStep(() => udpServer?.Dispose(), "stopping the UDP listener");
            foreach (SessionGroup session in sessions)
            {
                TryShutdownStep(session.Client.Disconnect, "disconnecting a client");
            }
        }
        finally
        {
            try
            {
                TryShutdownStep(
                    () => acceptTask?.Wait(TimeSpan.FromSeconds(2)),
                    "waiting for the accept loop");
            }
            finally
            {
                TryShutdownStep(() => stop?.Dispose(), "disposing the cancellation source");
                SafeLog("[P236] Server stopped.");
            }
        }
    }

    public static void RemoveSession(SessionGroup session)
    {
        lock (SyncRoot)
        {
            if (!Sessions.Remove(session)) return;
            SaveProfileLocked(session.Profile);
        }
    }

    public static string AssignLoginUsername(SessionGroup session, string username)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("A login username is required.", nameof(username));
        string sourceUsername = username.Trim();
        lock (SyncRoot)
        {
            if (ProfilesByUsername.TryGetValue(sourceUsername, out LegacySessionProfile? stored))
            {
                session.RebindProfile(stored);
                LoginProfiles[stored.UserNo] = stored;
                session.Client.Nickname = stored.Nickname;
                return stored.UserId;
            }

            uint userNo = AllocateUserNoLocked();
            string identity = sourceUsername;
            for (int suffix = 2; HasIdentityLocked(identity); suffix++) identity = $"{sourceUsername}_{suffix}";
            LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(userNo);
            profile.SourceUsername = sourceUsername;
            profile.UserId = identity;
            profile.Nickname = identity;
            _profileStore!.Save(LegacyProfileRecord.FromProfile(profile));
            ProfilesByUserNo.Add(userNo, profile);
            ProfilesByUsername.Add(sourceUsername, profile);
            LoginProfiles[userNo] = profile;
            session.RebindProfile(profile);
            session.Client.Nickname = profile.Nickname;
            LegacyPacketTrace.LogEvent($"[P236 PROFILE] Created '{sourceUsername}' as user {userNo}.");
            return identity;
        }
    }

    public static void SaveProfile(SessionGroup session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (SyncRoot) SaveProfileLocked(session.Profile);
    }

    public static void UpdateLicenseProgress(
        SessionGroup session,
        byte updatedRow,
        ushort[] completionMasks)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(completionMasks);
        if (completionMasks.Length != P236LicenseProgress.CompletionMaskCount)
            throw new ArgumentException(
                $"Exactly {P236LicenseProgress.CompletionMaskCount} license completion masks are required.",
                nameof(completionMasks));

        lock (SyncRoot)
        {
            P236LicenseProgress.FloorSnapshot licenseProgressFloor =
                _licenseProgressFloor ??
                throw new InvalidOperationException("The P236 listener is not running.");
            LegacySessionProfile profile = session.Profile;
            lock (profile.PersistenceSyncRoot)
            {
                ushort[] mergedMasks = profile.GetLicenseCompletionMasks();
                for (int index = 0; index < mergedMasks.Length; index++)
                    mergedMasks[index] |= completionMasks[index];

                if (updatedRow != byte.MaxValue && profile.LicenseLevel < updatedRow)
                    profile.LicenseLevel = updatedRow;
                profile.SetLicenseCompletionMasks(mergedMasks);
                licenseProgressFloor.Apply(profile);
                SaveProfileLocked(profile);
            }
        }
    }

    public static bool TryBindChannelProfile(SessionGroup currentSession, uint claimedUserNo)
    {
        if (claimedUserNo == 0) return false;
        lock (SyncRoot)
        {
            if (!Sessions.Contains(currentSession)) return false;
            LegacySessionProfile? profile = null;
            if (currentSession.Profile.UserNo == claimedUserNo && currentSession.Profile.SourceUsername is not null)
                profile = currentSession.Profile;
            else if (!LoginProfiles.TryGetValue(claimedUserNo, out profile))
                profile = Sessions.Select(candidate => candidate.Profile)
                    .FirstOrDefault(candidate => candidate.UserNo == claimedUserNo && candidate.SourceUsername is not null);
            if (profile is null) return false;

            foreach (SessionGroup source in Sessions)
            {
                if (ReferenceEquals(source, currentSession) || !ReferenceEquals(source.Profile, profile)) continue;
                currentSession.Multiplayer.Channel = source.Multiplayer.Channel;
                currentSession.Multiplayer.ChannelToken = source.Multiplayer.ChannelToken;
                currentSession.Multiplayer.ReportedUdpEndPoint ??= Clone(source.Multiplayer.ReportedUdpEndPoint);
                currentSession.Multiplayer.ObservedUdpEndPoint ??= Clone(source.Multiplayer.ObservedUdpEndPoint);
            }
            if (ObservedUdpEndPoints.TryGetValue(claimedUserNo, out IPEndPoint? observed))
                currentSession.Multiplayer.ObservedUdpEndPoint = Clone(observed);
            currentSession.RebindProfile(profile);
            currentSession.Client.Nickname = profile.Nickname;
            currentSession.Multiplayer.UserNo = claimedUserNo;
            LoginProfiles[claimedUserNo] = profile;
            return true;
        }
    }

    public static void ObserveUdpEndPoint(uint userNo, IPEndPoint endpoint)
    {
        if (userNo == 0 || endpoint.Port == 0) return;
        lock (SyncRoot)
        {
            ObservedUdpEndPoints[userNo] = Clone(endpoint)!;
            foreach (SessionGroup session in Sessions.Where(value => value.Profile.UserNo == userNo))
                session.Multiplayer.ObservedUdpEndPoint = Clone(endpoint);
        }
    }

    private static async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Socket socket;
            try { socket = await listener.AcceptSocketAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException exception)
            {
                LegacyPacketTrace.LogEvent($"[P236] Accept failed: {exception.Message}");
                continue;
            }

            SessionGroup session = new(socket, LegacySessionProfile.CreateFromStaticTemplate(0));
            lock (SyncRoot)
            {
                if (!ReferenceEquals(_listener, listener)) { socket.Dispose(); continue; }
                Sessions.Add(session);
            }
            try
            {
                ProtocolResponses.SendFirstMessage(session);
                session.Client.Start();
            }
            catch (Exception exception)
            {
                session.Client.Disconnect();
                LegacyPacketTrace.LogEvent($"[P236] Could not initialize a client: {exception.Message}");
            }
        }
    }

    private static void LoadProfilesLocked(
        string path,
        P236LicenseProgress.FloorSnapshot licenseProgressFloor)
    {
        JsonLegacyProfileStore store = new(path);
        List<LegacyProfileRecord> migratedRecords = [];
        foreach (LegacyProfileRecord record in store.LoadAll())
        {
            LegacySessionProfile profile = record.ToProfile();
            if (licenseProgressFloor.Apply(profile))
            {
                migratedRecords.Add(LegacyProfileRecord.FromProfile(profile));
            }
            ProfilesByUserNo.Add(profile.UserNo, profile);
            ProfilesByUsername.Add(profile.SourceUsername, profile);
        }
        if (migratedRecords.Count != 0)
        {
            store.SaveAll(migratedRecords);
            LegacyPacketTrace.LogEvent(
                $"[P236 PROFILE] Applied the configured license progress floor to " +
                $"{migratedRecords.Count} persisted profile(s).");
        }
        _profileStore = store;
        LegacyPacketTrace.LogEvent($"[P236 PROFILE] Loaded {ProfilesByUserNo.Count} profile(s) from '{path}'.");
    }

    private static void SaveProfileLocked(LegacySessionProfile profile)
    {
        if (_profileStore is null || string.IsNullOrWhiteSpace(profile.SourceUsername)) return;
        if (!ProfilesByUserNo.TryGetValue(profile.UserNo, out LegacySessionProfile? stored) || !ReferenceEquals(stored, profile)) return;
        try
        {
            lock (profile.PersistenceSyncRoot)
                _profileStore.Save(LegacyProfileRecord.FromProfile(profile));
        }
        catch (Exception exception) { LegacyPacketTrace.LogEvent($"[P236 PROFILE] Save failed: {exception.Message}"); }
    }

    private static void SaveAllProfilesLocked()
    {
        if (_profileStore is null) return;
        try
        {
            List<LegacyProfileRecord> records = [];
            foreach (LegacySessionProfile profile in ProfilesByUserNo.Values)
            {
                lock (profile.PersistenceSyncRoot)
                    records.Add(LegacyProfileRecord.FromProfile(profile));
            }
            _profileStore.SaveAll(records);
        }
        catch (Exception exception) { LegacyPacketTrace.LogEvent($"[P236 PROFILE] Flush failed: {exception.Message}"); }
    }

    private static uint AllocateUserNoLocked()
    {
        uint first = ServerRuntime.Options.FirstUserNumber;
        for (int attempt = 0; attempt < ProfilesByUserNo.Count + Sessions.Count + 1024; attempt++)
        {
            uint candidate = unchecked(first + _nextSessionOrdinal++);
            if (candidate != 0 && !ProfilesByUserNo.ContainsKey(candidate) && Sessions.All(s => s.Profile.UserNo != candidate))
                return candidate;
        }
        throw new InvalidOperationException("Unable to allocate a P236 user number.");
    }

    private static bool HasIdentityLocked(string identity) => ProfilesByUserNo.Values.Any(profile =>
        string.Equals(profile.UserId, identity, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(profile.Nickname, identity, StringComparison.OrdinalIgnoreCase));

    private static IPEndPoint? Clone(IPEndPoint? endpoint) =>
        endpoint is null ? null : new IPEndPoint(endpoint.Address, endpoint.Port);

    private static void TryShutdownStep(Action action, string description)
    {
        try { action(); }
        catch (Exception exception) { SafeLog($"[P236] Error while {description}: {exception.Message}"); }
    }

    private static void SafeLog(string message)
    {
        try { LegacyPacketTrace.LogEvent(message); }
        catch { }
    }
}
