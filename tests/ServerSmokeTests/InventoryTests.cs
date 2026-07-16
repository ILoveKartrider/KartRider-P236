using System.Net;
using System.Net.Sockets;
using KartRider;
using KartRider.Common.Utilities;
using KartRider.IO;

namespace KartRider.P236.Server.Tests;

public sealed class InventoryTests
{
    [Fact]
    public void KartInventoryStartsWithBasicPracticeKart()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        using ConnectedSocketPair sockets = ConnectedSocketPair.Create();
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        SessionGroup session = new(sockets.Server, profile);

        ProtocolResponses.SendInventory(session);

        using InPacket packet = new(ReceiveClearPayload(sockets.Peer));
        Assert.Equal(
            Adler32Helper.GenerateAdler32_ASCII("LoRpGetRiderItemPacket"),
            packet.ReadUInt());
        Assert.Equal(73, packet.ReadInt());
        for (short expectedItem = 0; expectedItem <= 72; expectedItem++)
        {
            Assert.Equal(3, packet.ReadShort());
            Assert.Equal(expectedItem, packet.ReadShort());
            Assert.Equal(1, packet.ReadShort());
            Assert.Equal(0, packet.ReadByte());
            Assert.Equal(0, packet.ReadByte());
            Assert.Equal(-1, packet.ReadShort());
            Assert.Equal(0, packet.ReadShort());
        }
        Assert.Equal(0, packet.Available);
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
                throw new EndOfStreamException("Socket closed inside an inventory test frame.");
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
