using System.Net;
using System.Net.Sockets;
using KartRider;
using KartRider.Common.Utilities;
using KartRider.IO;

namespace KartRider.P236.Server.Tests;

public sealed class LicenseProgressTests
{
    private static readonly ushort[] L1CompletionMasks = [31, 7, 31, 63, 21, 0];

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
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "l1-rider";
        SessionGroup session = new(socket, profile);
        const uint rewardId = 759;

        using OutPacket body = new();
        body.WriteByte(P236LicenseProgress.MaximumLevel);
        foreach (ushort mask in L1CompletionMasks)
        {
            body.WriteUShort(mask);
        }
        body.WriteUInt(rewardId);
        using InPacket packet = new(body.ToArray());

        LegacyPacketHandlers.HandleUpdateCompletion(session, packet);

        Assert.Equal(P236LicenseProgress.MaximumLevel, profile.LicenseLevel);
        Assert.Equal(L1CompletionMasks, profile.GetLicenseCompletionMasks());
        Assert.Equal(P236LicenseProgress.MaximumLevel, session.SingleRace.LastCompletionRow);
        Assert.Equal(rewardId, session.SingleRace.LastCompletionRewardId);
        Assert.Equal(0, packet.Available);
    }

    [Fact]
    public void CompletionMaskUpdateCanRecordL1StagesBeforeLicenseAward()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "l1-candidate";
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
    public void CompletionPacketRejectsLevelAboveL1()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        SessionGroup session = new(socket, profile);

        using OutPacket body = new();
        body.WriteByte(P236LicenseProgress.MaximumLevel + 1);
        foreach (ushort mask in L1CompletionMasks)
        {
            body.WriteUShort(mask);
        }
        body.WriteUInt(0);
        using InPacket packet = new(body.ToArray());

        Assert.Throws<PacketReadException>(
            () => LegacyPacketHandlers.HandleUpdateCompletion(session, packet));
        Assert.Equal(3, profile.LicenseLevel);
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
        profile.SetLicenseCompletionMasks(L1CompletionMasks);
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
        Assert.Equal(L1CompletionMasks, masks);
    }

    private static byte[] ReceiveClearPayload(Socket socket)
    {
        byte[] header = ReceiveExactly(socket, 4);
        int remainingLength = P236FrameCodec.DecodeRemainingLength(header, iv: 0);
        byte[] remainder = ReceiveExactly(socket, remainingLength);
        uint iv = 0;
        return P236FrameCodec.DecodePayload(remainder, ref iv);
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
