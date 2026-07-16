using System.Net;
using System.Net.Sockets;
using KartRider.IO;

namespace KartRider.P236.Server.Tests;

public sealed class RoomChatTests
{
    [Fact]
    public void RoomChatRelaysUnicodeToInitializedRidersAndObserversWithoutSenderEcho()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        LegacyObserverPolicy.Reload();

        using TestClient owner = TestClient.Create("chat-owner", 1);
        using TestClient filler1 = TestClient.Create("chat-filler-1", 2);
        using TestClient filler2 = TestClient.Create("chat-filler-2", 3);
        using TestClient filler3 = TestClient.Create("chat-filler-3", 4);
        using TestClient sender = TestClient.Create("chat-sender", 5);
        using TestClient observer = TestClient.Create("observer", 6);

        LegacyRoom room = LegacyRoomManager.Create(
            owner.Session,
            "room-chat-relay",
            string.Empty,
            gameType: 3,
            channel: 1,
            speedType: 0,
            unknown1: 0,
            unknown2: 0,
            trackCandidates: new byte[32],
            integrityProof: new byte[21]);

        try
        {
            Assert.True(LegacyRoomManager.TryJoin(
                filler1.Session, room.Id, string.Empty, out _, out LegacyRoomMember filler1Member));
            Assert.True(LegacyRoomManager.TryJoin(
                filler2.Session, room.Id, string.Empty, out _, out LegacyRoomMember filler2Member));
            Assert.True(LegacyRoomManager.TryJoin(
                filler3.Session, room.Id, string.Empty, out _, out LegacyRoomMember filler3Member));
            Assert.True(LegacyRoomManager.TryJoin(
                sender.Session, room.Id, string.Empty, out _, out LegacyRoomMember senderMember));
            Assert.True(LegacyRoomManager.TryJoin(
                observer.Session, room.Id, string.Empty, out _, out LegacyRoomMember observerMember));

            Assert.Equal(4, senderMember.SlotId);
            Assert.True(observerMember.IsObserver);
            Assert.Equal(8, observerMember.SlotId);

            LegacyRoomMember ownerMember = room.Members[0];
            lock (room.SyncRoot)
            {
                ownerMember.Initialized = true;
                senderMember.Initialized = true;
                observerMember.Initialized = true;
                Assert.False(filler1Member.Initialized);
                Assert.False(filler2Member.Initialized);
                Assert.False(filler3Member.Initialized);
            }

            const string message = "안녕 🚗";
            using OutPacket request = new("GrRiderTalkPacket");
            request.WriteString(message);
            request.WriteUInt(2);
            using InPacket incoming = new(request.ToArray());

            PacketDispatcher.Handle(sender.Session, incoming);

            AssertRoomChatEcho(owner.Sockets.Peer, senderMember.SlotId, message);
            AssertRoomChatEcho(observer.Sockets.Peer, senderMember.SlotId, message);
            AssertNoFrame(sender.Sockets.Peer, "The sender received a duplicate room-chat echo.");
            AssertNoFrame(filler1.Sockets.Peer, "An uninitialized rider received room chat.");
            AssertNoFrame(filler2.Sockets.Peer, "An uninitialized rider received room chat.");
            AssertNoFrame(filler3.Sockets.Peer, "An uninitialized rider received room chat.");
        }
        finally
        {
            _ = LegacyRoomManager.Remove(observer.Session);
            _ = LegacyRoomManager.Remove(sender.Session);
            _ = LegacyRoomManager.Remove(filler3.Session);
            _ = LegacyRoomManager.Remove(filler2.Session);
            _ = LegacyRoomManager.Remove(filler1.Session);
            _ = LegacyRoomManager.Remove(owner.Session);
        }
    }

    [Fact]
    public void RoomChatRejectsOversizedAndMalformedPackets()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        LegacyObserverPolicy.Reload();

        using TestClient owner = TestClient.Create("chat-validation-owner", 11);
        using TestClient target = TestClient.Create("chat-validation-target", 12);
        LegacyRoom room = LegacyRoomManager.Create(
            owner.Session,
            "room-chat-validation",
            string.Empty,
            gameType: 0,
            channel: 1,
            speedType: 0,
            unknown1: 0,
            unknown2: 0,
            trackCandidates: new byte[32],
            integrityProof: new byte[21]);

        try
        {
            Assert.True(LegacyRoomManager.TryJoin(
                target.Session, room.Id, string.Empty, out _, out LegacyRoomMember targetMember));
            lock (room.SyncRoot)
            {
                room.Members[0].Initialized = true;
                targetMember.Initialized = true;
            }

            using (OutPacket oversized = new("GrRiderTalkPacket"))
            {
                oversized.WriteString(new string('x', 256));
                oversized.WriteUInt(0);
                using InPacket incoming = new(oversized.ToArray());
                PacketDispatcher.Handle(owner.Session, incoming);
            }
            AssertNoFrame(target.Sockets.Peer, "A 256-character room-chat message was relayed.");

            using OutPacket malformed = new("GrRiderTalkPacket");
            malformed.WriteString("missing trailing flags");
            using InPacket malformedIncoming = new(malformed.ToArray());
            Assert.Throws<InvalidOperationException>(() =>
                PacketDispatcher.Handle(owner.Session, malformedIncoming));
            AssertNoFrame(target.Sockets.Peer, "A malformed room-chat message was relayed.");
        }
        finally
        {
            _ = LegacyRoomManager.Remove(target.Session);
            _ = LegacyRoomManager.Remove(owner.Session);
        }
    }

    private static void AssertRoomChatEcho(Socket socket, int expectedSenderSlot, string expectedMessage)
    {
        using InPacket echo = new(ReceiveClearPayload(socket));
        Assert.Equal(0x395F0686u, echo.ReadUInt());
        Assert.Equal(expectedSenderSlot, echo.ReadInt());
        Assert.Equal(expectedMessage, echo.ReadString());
        Assert.Equal(0, echo.Available);
    }

    private static void AssertNoFrame(Socket socket, string failureMessage) =>
        Assert.False(socket.Poll(100_000, SelectMode.SelectRead), failureMessage);

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

    private sealed class TestClient : IDisposable
    {
        private TestClient(ConnectedSocketPair sockets, SessionGroup session)
        {
            Sockets = sockets;
            Session = session;
        }

        public ConnectedSocketPair Sockets { get; }

        public SessionGroup Session { get; }

        public static TestClient Create(string sourceUsername, uint userNo)
        {
            ConnectedSocketPair sockets = ConnectedSocketPair.Create();
            try
            {
                LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(userNo);
                profile.SourceUsername = sourceUsername;
                SessionGroup session = new(sockets.Server, profile);
                session.Multiplayer.Channel = 1;
                return new TestClient(sockets, session);
            }
            catch
            {
                sockets.Dispose();
                throw;
            }
        }

        public void Dispose() => Sockets.Dispose();
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
