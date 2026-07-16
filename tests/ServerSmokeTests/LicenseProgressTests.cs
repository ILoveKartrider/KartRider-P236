using System.Net;
using System.Net.Sockets;
using KartRider;
using KartRider.Common.Utilities;
using KartRider.IO;

namespace KartRider.P236.Server.Tests;

public sealed class LicenseProgressTests
{
    private static readonly ushort[] ReplayableL1MissionAccessMasks = [31, 7, 31, 63, 30, 0];
    private static readonly ushort[] SampleL1ProgressMasks = [31, 7, 31, 63, 21, 0];

    [Fact]
    public void DefaultProfilesExposeReplayableL1MissionAccess()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);

        Assert.Equal(P236LicenseProgress.MaximumLevel, options.DefaultLicenseLevel);
        Assert.Equal(ReplayableL1MissionAccessMasks, options.DefaultLicenseCompletionMasks);
        Assert.True(options.EnforceDefaultLicenseProgressFloor);

        ServerRuntime.Configure(options);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);

        Assert.Equal(P236LicenseProgress.MaximumLevel, profile.LicenseLevel);
        Assert.Equal(ReplayableL1MissionAccessMasks, profile.GetLicenseCompletionMasks());
    }

    [Fact]
    public void ConfiguredProgressFloorPromotesAndPreservesProfileData()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        ServerRuntime.Configure(options);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "alice";
        profile.UserId = "alice-id";
        profile.Nickname = "Alice";
        profile.RiderIntro = "hello";
        profile.RP = 123_456;
        profile.PMap = 7;
        profile.Lucci = 654_321;
        profile.SlotChanger = 123;
        profile.Equipment.Character = 9;
        profile.Equipment.Kart = 17;
        profile.LicenseLevel = 3;
        profile.SetLicenseCompletionMasks([31, 7, 31, 63, 2, 64]);

        Assert.True(P236LicenseProgress.ApplyConfiguredProgressFloor(profile, options));

        Assert.Equal(P236LicenseProgress.MaximumLevel, profile.LicenseLevel);
        Assert.Equal([31, 7, 31, 63, 30, 64], profile.GetLicenseCompletionMasks());
        Assert.Equal("alice", profile.SourceUsername);
        Assert.Equal("alice-id", profile.UserId);
        Assert.Equal("Alice", profile.Nickname);
        Assert.Equal("hello", profile.RiderIntro);
        Assert.Equal(123_456, profile.RP);
        Assert.Equal((uint)7, profile.PMap);
        Assert.Equal((uint)654_321, profile.Lucci);
        Assert.Equal((short)123, profile.SlotChanger);
        Assert.Equal((ushort)9, profile.Equipment.Character);
        Assert.Equal((ushort)17, profile.Equipment.Kart);
        Assert.False(P236LicenseProgress.ApplyConfiguredProgressFloor(profile, options));
    }

    [Fact]
    public void ConfiguredProgressFloorCanBeDisabled()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        options.EnforceDefaultLicenseProgressFloor = false;
        ServerRuntime.Configure(options);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.LicenseLevel = 3;
        profile.SetLicenseCompletionMasks([31, 7, 31, 63, 2, 0]);

        Assert.False(P236LicenseProgress.ApplyConfiguredProgressFloor(profile, options));
        Assert.Equal(3, profile.LicenseLevel);
        Assert.Equal([31, 7, 31, 63, 2, 0], profile.GetLicenseCompletionMasks());
    }

    [Fact]
    public void ServerOptionsAcceptL1AndRejectHigherLevels()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        options.DefaultLicenseLevel = P236LicenseProgress.MaximumLevel;

        options.Validate();

        options.DefaultLicenseLevel = P236LicenseProgress.MaximumLevel + 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void CompletionPacketAcceptsL1AndKeepsSixMaskWireShape()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        options.EnforceDefaultLicenseProgressFloor = false;
        using P236Server server = StartServer(options);
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "l1-rider";
        profile.LicenseLevel = 0;
        profile.SetLicenseCompletionMasks([0, 0, 0, 0, 0, 0]);
        SessionGroup session = new(socket, profile);
        const uint rewardId = 759;

        using OutPacket body = new();
        body.WriteByte(P236LicenseProgress.MaximumLevel);
        foreach (ushort mask in SampleL1ProgressMasks)
        {
            body.WriteUShort(mask);
        }
        body.WriteUInt(rewardId);
        using InPacket packet = new(body.ToArray());

        LegacyPacketHandlers.HandleUpdateCompletion(session, packet);

        Assert.Equal(P236LicenseProgress.MaximumLevel, profile.LicenseLevel);
        Assert.Equal(SampleL1ProgressMasks, profile.GetLicenseCompletionMasks());
        Assert.Equal(P236LicenseProgress.MaximumLevel, session.SingleRace.LastCompletionRow);
        Assert.Equal(rewardId, session.SingleRace.LastCompletionRewardId);
        Assert.Equal(0, packet.Available);
    }

    [Fact]
    public void CompletionMaskUpdateCanRecordL1StagesBeforeLicenseAward()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        options.DefaultLicenseLevel = 3;
        options.EnforceDefaultLicenseProgressFloor = false;
        using P236Server server = StartServer(options);
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "l1-candidate";
        profile.SetLicenseCompletionMasks([0, 0, 0, 0, 0, 0]);
        SessionGroup session = new(socket, profile);
        ushort[] inProgressMasks = [31, 7, 31, 63, 1, 0];

        using OutPacket body = new();
        body.WriteByte(byte.MaxValue);
        foreach (ushort mask in inProgressMasks)
        {
            body.WriteUShort(mask);
        }
        body.WriteUInt(0);
        using InPacket packet = new(body.ToArray());

        LegacyPacketHandlers.HandleUpdateCompletion(session, packet);

        Assert.Equal(3, profile.LicenseLevel);
        Assert.Equal(inProgressMasks, profile.GetLicenseCompletionMasks());
        Assert.Equal(byte.MaxValue, session.SingleRace.LastCompletionRow);
    }

    [Fact]
    public void CompletionUpdateReappliesConfiguredProgressFloor()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        using P236Server server = StartServer(options);
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "existing-rider";
        profile.LicenseLevel = 3;
        profile.SetLicenseCompletionMasks([31, 7, 31, 63, 2, 0]);
        SessionGroup session = new(socket, profile);

        using OutPacket body = new();
        body.WriteByte(3);
        foreach (ushort mask in new ushort[] { 31, 7, 31, 63, 2, 0 })
        {
            body.WriteUShort(mask);
        }
        body.WriteUInt(0);
        using InPacket packet = new(body.ToArray());

        LegacyPacketHandlers.HandleUpdateCompletion(session, packet);

        Assert.Equal(P236LicenseProgress.MaximumLevel, profile.LicenseLevel);
        Assert.Equal(ReplayableL1MissionAccessMasks, profile.GetLicenseCompletionMasks());
        Assert.Equal(3, session.SingleRace.LastCompletionRow);
    }

    [Fact]
    public void CompletionUpdateNeverRegressesExistingLicenseProgress()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        options.EnforceDefaultLicenseProgressFloor = false;
        using P236Server server = StartServer(options);
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "advanced-rider";
        profile.LicenseLevel = P236LicenseProgress.MaximumLevel;
        profile.SetLicenseCompletionMasks([31, 7, 31, 63, 32, 64]);
        SessionGroup session = new(socket, profile);

        using OutPacket body = new();
        body.WriteByte(3);
        foreach (ushort mask in new ushort[] { 31, 7, 31, 63, 2, 0 })
        {
            body.WriteUShort(mask);
        }
        body.WriteUInt(0);
        using InPacket packet = new(body.ToArray());

        LegacyPacketHandlers.HandleUpdateCompletion(session, packet);

        Assert.Equal(P236LicenseProgress.MaximumLevel, profile.LicenseLevel);
        Assert.Equal([31, 7, 31, 63, 34, 64], profile.GetLicenseCompletionMasks());
    }

    [Fact]
    public async Task RunningServerUsesCapturedFloorAfterOptionsMutationAndFailedSecondStart()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions runningOptions = TestOptions.Create(Path.Combine(temporary.Path, "running"));
        runningOptions.DefaultLicenseLevel = 2;
        runningOptions.DefaultLicenseCompletionMasks = [0, 0, 0, 0, 1, 0];
        await using P236Server runningServer = new();
        await runningServer.StartAsync(runningOptions);

        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        SessionGroup session = new(socket, LegacySessionProfile.CreateFromStaticTemplate(0));
        RouterListener.AssignLoginUsername(session, "captured-floor-rider");
        LegacySessionProfile profile = session.Profile;
        profile.LicenseLevel = 0;
        profile.SetLicenseCompletionMasks([0, 0, 0, 0, 0, 0]);
        RouterListener.SaveProfile(session);

        runningOptions.DefaultLicenseLevel = P236LicenseProgress.MaximumLevel;
        runningOptions.DefaultLicenseCompletionMasks =
            [ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue];
        ApplyCompletion(session, 1, [0, 0, 0, 0, 0, 1]);

        Assert.Equal(2, profile.LicenseLevel);
        Assert.Equal([0, 0, 0, 0, 1, 1], profile.GetLicenseCompletionMasks());

        profile.LicenseLevel = 0;
        profile.SetLicenseCompletionMasks([0, 0, 0, 0, 0, 0]);
        RouterListener.SaveProfile(session);

        P236ServerOptions rejectedOptions = TestOptions.Create(Path.Combine(temporary.Path, "rejected"));
        rejectedOptions.EnforceDefaultLicenseProgressFloor = false;
        await using (P236Server rejectedServer = new())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => rejectedServer.StartAsync(rejectedOptions));
        }

        ApplyCompletion(session, 1, [0, 0, 0, 0, 0, 2]);

        Assert.Equal(2, profile.LicenseLevel);
        Assert.Equal([0, 0, 0, 0, 1, 2], profile.GetLicenseCompletionMasks());
        LegacyProfileRecord persisted = Assert.Single(
            new JsonLegacyProfileStore(
                Path.Combine(runningOptions.DataDirectory, "profiles.json")).LoadAll());
        Assert.Equal(2, persisted.LicenseLevel);
        Assert.Equal([0, 0, 0, 0, 1, 2], persisted.LicenseCompletionMasks);
    }

    [Fact]
    public async Task ConcurrentCompletionUpdatesOnRegisteredSharedProfilePersistUnion()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        options.EnforceDefaultLicenseProgressFloor = false;
        await using P236Server server = new();
        await server.StartAsync(options);
        using Socket firstSocket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using Socket secondSocket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        SessionGroup firstSession = new(
            firstSocket,
            LegacySessionProfile.CreateFromStaticTemplate(0));
        RouterListener.AssignLoginUsername(firstSession, "shared-rider");
        LegacySessionProfile profile = firstSession.Profile;
        profile.LicenseLevel = 1;
        profile.SetLicenseCompletionMasks([0, 0, 0, 0, 0, 0]);
        RouterListener.SaveProfile(firstSession);

        SessionGroup secondSession = new(
            secondSocket,
            LegacySessionProfile.CreateFromStaticTemplate(0));
        RouterListener.AssignLoginUsername(secondSession, "shared-rider");
        Assert.Same(profile, secondSession.Profile);
        using Barrier barrier = new(2);

        Task firstUpdate = Task.Run(() =>
        {
            using OutPacket body = new();
            body.WriteByte(2);
            foreach (ushort mask in new ushort[] { 0, 0, 0, 0, 0, 1 })
            {
                body.WriteUShort(mask);
            }
            body.WriteUInt(0);
            using InPacket packet = new(body.ToArray());
            barrier.SignalAndWait();
            LegacyPacketHandlers.HandleUpdateCompletion(firstSession, packet);
        });
        Task secondUpdate = Task.Run(() =>
        {
            using OutPacket body = new();
            body.WriteByte(3);
            foreach (ushort mask in new ushort[] { 0, 0, 0, 0, 0, 2 })
            {
                body.WriteUShort(mask);
            }
            body.WriteUInt(0);
            using InPacket packet = new(body.ToArray());
            barrier.SignalAndWait();
            LegacyPacketHandlers.HandleUpdateCompletion(secondSession, packet);
        });

        await Task.WhenAll(firstUpdate, secondUpdate);

        Assert.Equal(3, profile.LicenseLevel);
        Assert.Equal([0, 0, 0, 0, 0, 3], profile.GetLicenseCompletionMasks());

        LegacyProfileRecord persisted = Assert.Single(
            new JsonLegacyProfileStore(
                Path.Combine(options.DataDirectory, "profiles.json")).LoadAll());
        Assert.Equal("shared-rider", persisted.SourceUsername);
        Assert.Equal(3, persisted.LicenseLevel);
        Assert.Equal([0, 0, 0, 0, 0, 3], persisted.LicenseCompletionMasks);
    }

    [Fact]
    public void CompletionPacketRejectsLevelAboveL1()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        SessionGroup session = new(socket, profile);

        using OutPacket body = new();
        body.WriteByte(P236LicenseProgress.MaximumLevel + 1);
        foreach (ushort mask in SampleL1ProgressMasks)
        {
            body.WriteUShort(mask);
        }
        body.WriteUInt(0);
        using InPacket packet = new(body.ToArray());

        Assert.Throws<PacketReadException>(
            () => LegacyPacketHandlers.HandleUpdateCompletion(session, packet));
        Assert.Equal(P236LicenseProgress.MaximumLevel, profile.LicenseLevel);
    }

    [Fact]
    public void ProfileRejectsUnsupportedLevelAndMalformedCompletionMasks()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => profile.LicenseLevel = P236LicenseProgress.MaximumLevel + 1);
        Assert.Throws<ArgumentException>(() => profile.SetLicenseCompletionMasks([1, 2, 3]));
    }

    [Fact]
    public void RiderResponseSerializesL1AndAllSixCompletionMasks()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        using ConnectedSocketPair sockets = ConnectedSocketPair.Create();
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "l1-rider";
        profile.Nickname = "L1 Rider";
        profile.LicenseLevel = P236LicenseProgress.MaximumLevel;
        profile.SetLicenseCompletionMasks(SampleL1ProgressMasks);
        SessionGroup session = new(sockets.Server, profile);

        ProtocolResponses.SendRider(session);

        using InPacket packet = new(ReceiveClearPayload(sockets.Peer));
        Assert.Equal(
            Adler32Helper.GenerateAdler32_ASCII("LoRpGetRiderPacket"),
            packet.ReadUInt());
        Assert.Equal(1, packet.ReadByte());
        Assert.Equal("L1 Rider", packet.ReadString());
        _ = packet.ReadInt();
        _ = packet.ReadShort();
        _ = packet.ReadShort();
        Assert.Equal(P236LicenseProgress.MaximumLevel, packet.ReadByte());
        ushort[] masks = new ushort[P236LicenseProgress.CompletionMaskCount];
        for (int i = 0; i < masks.Length; i++)
        {
            masks[i] = packet.ReadUShort();
        }
        Assert.Equal(SampleL1ProgressMasks, masks);
    }

    private static byte[] ReceiveClearPayload(Socket socket)
    {
        byte[] header = ReceiveExactly(socket, 4);
        int remainingLength = P236FrameCodec.DecodeRemainingLength(header, iv: 0);
        byte[] remainder = ReceiveExactly(socket, remainingLength);
        uint iv = 0;
        return P236FrameCodec.DecodePayload(remainder, ref iv);
    }

    private static P236Server StartServer(P236ServerOptions options)
    {
        P236Server server = new();
        try
        {
            server.StartAsync(options).GetAwaiter().GetResult();
            return server;
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    private static void ApplyCompletion(
        SessionGroup session,
        byte updatedRow,
        IReadOnlyList<ushort> completionMasks)
    {
        Assert.Equal(P236LicenseProgress.CompletionMaskCount, completionMasks.Count);
        using OutPacket body = new();
        body.WriteByte(updatedRow);
        foreach (ushort mask in completionMasks)
        {
            body.WriteUShort(mask);
        }
        body.WriteUInt(0);
        using InPacket packet = new(body.ToArray());
        LegacyPacketHandlers.HandleUpdateCompletion(session, packet);
        Assert.Equal(0, packet.Available);
    }

    private static byte[] ReceiveExactly(Socket socket, int length)
    {
        byte[] result = new byte[length];
        int offset = 0;
        while (offset < result.Length)
        {
            int received = socket.Receive(result, offset, result.Length - offset, SocketFlags.None);
            if (received == 0)
            {
                throw new EndOfStreamException("Socket closed inside a P236 test frame.");
            }
            offset += received;
        }
        return result;
    }

    private sealed class ConnectedSocketPair : IDisposable
    {
        private ConnectedSocketPair(Socket server, Socket peer)
        {
            Server = server;
            Peer = peer;
            Peer.ReceiveTimeout = 2_000;
        }

        public Socket Server { get; }

        public Socket Peer { get; }

        public static ConnectedSocketPair Create()
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            Socket peer = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                peer.Connect((IPEndPoint)listener.LocalEndpoint);
                return new ConnectedSocketPair(listener.AcceptSocket(), peer);
            }
            catch
            {
                peer.Dispose();
                throw;
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Dispose()
        {
            Server.Dispose();
            Peer.Dispose();
        }
    }
}
