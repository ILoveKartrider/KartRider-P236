using System.Net;
using System.Net.Sockets;
using KartRider;
using KartRider.IO;

namespace KartRider.P236.Server.Tests;

public sealed class FlagItemRelayTests
{
    [Fact]
    public void FlagSpecificOperationsRelayCapturedEnvelopesByteForByte()
    {
        (string Name, uint Operation, uint BaseType, int RawLength)[] captures =
        [
            ("Ghost", 0x0D8B032B, 0x188F044A, 24),
            ("Mine state 1", 0x0A6B02AF, 0x145003CE, 72),
            ("Mine transition", 0x0A6B02AF, 0x145003CE, 25),
            ("Rollingbomb", 0x2954059D, 0x3B1206BC, 132)
        ];

        using ConnectedSocketPair ownerSockets = ConnectedSocketPair.Create();
        using ConnectedSocketPair targetSockets = ConnectedSocketPair.Create();
        LegacySessionProfile ownerProfile = LegacySessionProfile.CreateFromStaticTemplate(1);
        ownerProfile.SourceUsername = $"flag-relay-owner-{Guid.NewGuid():N}";
        LegacySessionProfile targetProfile = LegacySessionProfile.CreateFromStaticTemplate(2);
        targetProfile.SourceUsername = $"flag-relay-target-{Guid.NewGuid():N}";
        SessionGroup owner = new(ownerSockets.Server, ownerProfile);
        SessionGroup target = new(targetSockets.Server, targetProfile);
        owner.Multiplayer.Channel = 1;
        target.Multiplayer.Channel = 1;

        try
        {
            LegacyRoom room = LegacyRoomManager.Create(
                owner,
                "flag-item-relay",
                string.Empty,
                gameType: 7,
                channel: 1,
                speedType: 0,
                unknown1: 0,
                unknown2: 0,
                trackCandidates: new byte[32],
                integrityProof: new byte[21]);
            Assert.True(LegacyRoomManager.TryJoin(target, room.Id, string.Empty, out _, out LegacyRoomMember targetMember));

            LegacyRoomMember ownerMember = room.Members[room.OwnerSlot];
            lock (room.SyncRoot)
            {
                ownerMember.Initialized = true;
                ownerMember.LoadedInGame = true;
                targetMember.Initialized = true;
                targetMember.LoadedInGame = true;
                room.Started = true;
                room.StartSignalSent = true;
                room.RaceGridSlots = [ownerMember.SlotId, targetMember.SlotId];
            }

            uint recipientMask = 1u << targetMember.SlotId;
            foreach ((string name, uint operation, uint baseType, int rawLength) in captures)
            {
                byte[] payload = BuildGameSlot(
                    ownerMember.SlotId,
                    recipientMask,
                    operation,
                    baseType,
                    rawLength);
                using InPacket incoming = new(payload);

                PacketDispatcher.Handle(owner, incoming);

                Assert.Equal(payload, ReceiveClearPayload(targetSockets.Peer));
                Assert.False(
                    ownerSockets.Peer.Poll(100_000, SelectMode.SelectRead),
                    $"{name} was echoed to its sender instead of only remote slots.");
            }

            byte[] wrongBase = BuildGameSlot(
                ownerMember.SlotId,
                recipientMask,
                operation: 0x0D8B032B,
                baseType: 0x1CB30486,
                rawLength: 24);
            using (InPacket incoming = new(wrongBase))
            {
                PacketDispatcher.Handle(owner, incoming);
            }
            Assert.False(
                targetSockets.Peer.Poll(100_000, SelectMode.SelectRead),
                "A flag item operation with the wrong native base type was relayed.");
        }
        finally
        {
            _ = LegacyRoomManager.Remove(target);
            _ = LegacyRoomManager.Remove(owner);
        }
    }

    private static byte[] BuildGameSlot(
        int outerSlot,
        uint recipientMask,
        uint operation,
        uint baseType,
        int rawLength)
    {
        Assert.True(rawLength >= 8);
        using OutPacket packet = new("GameSlotPacket");
        packet.WriteUInt(checked((uint)outerSlot));
        packet.WriteUInt(recipientMask);
        packet.WriteBool(false);
        packet.WriteInt(rawLength);
        packet.WriteUInt(operation);
        packet.WriteUInt(baseType);
        for (int offset = 8; offset < rawLength; offset++)
        {
            packet.WriteByte(unchecked((byte)(offset * 37 + rawLength)));
        }
        return packet.ToArray();
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
