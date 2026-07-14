using System.Net.Sockets;

namespace KartRider;

internal sealed class SessionGroup
{
    private LegacySessionProfile _profile;
    public object m_lock { get; } = new();
    public uint TimeAttackStartTicks;
    public int SendPlaneCount = 6;
    public int TotalSendPlaneCount = 6;
    public byte PlaneCheck1;
    public LegacySingleRaceState SingleRace { get; } = new();
    public LegacyMultiplayerState Multiplayer { get; } = new();
    public LegacySessionProfile Profile => Volatile.Read(ref _profile);
    public ClientConnection Client { get; }

    public SessionGroup(Socket socket, LegacySessionProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Client = new ClientConnection(this, socket);
    }

    internal LegacySessionProfile RebindProfile(LegacySessionProfile profile) =>
        Interlocked.Exchange(ref _profile, profile ?? throw new ArgumentNullException(nameof(profile)));
}
