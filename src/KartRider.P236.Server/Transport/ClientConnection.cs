using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using KartRider.Common.Security;
using KartRider.IO;

namespace KartRider;

internal sealed class ClientConnection
{
    private const int MaximumPayloadLength = 1_048_576;
    private readonly SessionGroup _session;
    private readonly object _sendSync = new();
    private readonly CancellationTokenSource _stop = new();
    private int _disconnected;

    public Socket Socket { get; }
    public uint RIV { get; set; }
    public uint SIV { get; set; }
    public string Nickname { get; set; } = string.Empty;

    public ClientConnection(SessionGroup session, Socket socket)
    {
        _session = session;
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Socket.NoDelay = true;
    }

    public void Start() => _ = Task.Run(ReceiveLoopAsync);

    public void Send(OutPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        byte[] payload = packet.ToArray();
        LegacyPacketTrace.LogSend(payload);
        byte[] frame;
        lock (_sendSync)
        {
            if (Volatile.Read(ref _disconnected) != 0) return;
            uint sendIv = SIV;
            frame = P236FrameCodec.Encode(payload, ref sendIv);
            SIV = sendIv;
            try
            {
                int offset = 0;
                while (offset < frame.Length)
                {
                    int written = Socket.Send(frame, offset, frame.Length - offset, SocketFlags.None);
                    if (written == 0) throw new SocketException((int)SocketError.ConnectionReset);
                    offset += written;
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                _ = Task.Run(Disconnect);
            }
        }
    }

    public void Disconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0) return;
        try
        {
            try { _stop.Cancel(); }
            catch (Exception exception) { SafeLog($"[P236 SESSION] Cancellation failed: {exception.Message}"); }
            try { Socket.Shutdown(SocketShutdown.Both); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (Exception exception) { SafeLog($"[P236 SESSION] Socket shutdown failed: {exception.Message}"); }
            try { Socket.Dispose(); }
            catch (Exception exception) { SafeLog($"[P236 SESSION] Socket disposal failed: {exception.Message}"); }
        }
        finally
        {
            RunDisconnectCleanup(
                () => LegacyMultiplayerHandlers.HandleDisconnect(_session),
                () => RouterListener.RemoveSession(_session));
        }
    }

    internal static void RunDisconnectCleanup(Action roomCleanup, Action sessionRemoval)
    {
        ArgumentNullException.ThrowIfNull(roomCleanup);
        ArgumentNullException.ThrowIfNull(sessionRemoval);
        try
        {
            roomCleanup();
        }
        catch (Exception exception)
        {
            SafeLog($"[P236 SESSION] Room cleanup failed: {exception.Message}");
        }
        finally
        {
            try
            {
                sessionRemoval();
            }
            catch (Exception exception)
            {
                SafeLog($"[P236 SESSION] Session removal failed: {exception.Message}");
            }
        }
    }

    private static void SafeLog(string message)
    {
        try { LegacyPacketTrace.LogEvent(message); }
        catch { }
    }

    public IPEndPoint GetRemoteEndPoint()
    {
        try { return (IPEndPoint?)Socket.RemoteEndPoint ?? new IPEndPoint(IPAddress.None, 0); }
        catch (ObjectDisposedException) { return new IPEndPoint(IPAddress.None, 0); }
        catch (SocketException) { return new IPEndPoint(IPAddress.None, 0); }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            using NetworkStream stream = new(Socket, ownsSocket: false);
            byte[] header = new byte[4];
            while (!_stop.IsCancellationRequested)
            {
                if (!await ReadExactlyOrEofAsync(stream, header, _stop.Token).ConfigureAwait(false)) break;
                int remainingLength = P236FrameCodec.DecodeRemainingLength(header, RIV);
                if (remainingLength is < 0 or > MaximumPayloadLength + 4)
                    throw new InvalidDataException($"Invalid P236 frame length {remainingLength}.");
                byte[] remainder = new byte[remainingLength];
                if (!await ReadExactlyOrEofAsync(stream, remainder, _stop.Token).ConfigureAwait(false))
                    throw new EndOfStreamException("Connection ended inside a P236 frame.");
                uint receiveIv = RIV;
                byte[] payload = P236FrameCodec.DecodePayload(remainder, ref receiveIv);
                RIV = receiveIv;
                using InPacket packet = new(payload);
                PacketDispatcher.Handle(_session, packet);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            LegacyPacketTrace.LogEvent($"[P236 SESSION] {GetRemoteEndPoint()} closed: {exception.Message}");
        }
        finally { Disconnect(); }
    }

    private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), token).ConfigureAwait(false);
            if (read == 0) return offset == 0 ? false : throw new EndOfStreamException();
            offset += read;
        }
        return true;
    }
}

internal static class P236FrameCodec
{
    private const uint HeaderXor = 4_164_199_944u;
    private const uint ChecksumXor = 3_388_492_432u;
    private const uint IvStep = 21_446_425u;

    public static byte[] Encode(byte[] plaintext, ref uint iv)
    {
        if (iv == 0)
        {
            byte[] clearFrame = new byte[plaintext.Length + 4];
            BinaryPrimitives.WriteUInt32LittleEndian(clearFrame, checked((uint)plaintext.Length));
            plaintext.CopyTo(clearFrame, 4);
            return clearFrame;
        }

        uint currentIv = iv;
        byte[] encrypted = (byte[])plaintext.Clone();
        uint hash = KRPacketCrypto.HashEncrypt(encrypted, checked((uint)encrypted.Length), currentIv);
        byte[] frame = new byte[encrypted.Length + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, currentIv ^ checked((uint)encrypted.Length + 4) ^ HeaderXor);
        encrypted.CopyTo(frame, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(frame.Length - 4), currentIv ^ hash ^ ChecksumXor);
        AdvanceIv(ref iv);
        return frame;
    }

    public static int DecodeRemainingLength(ReadOnlySpan<byte> header, uint iv)
    {
        uint wire = BinaryPrimitives.ReadUInt32LittleEndian(header);
        uint length = iv == 0 ? wire : iv ^ wire ^ HeaderXor;
        if (length > int.MaxValue) throw new InvalidDataException($"P236 frame length {length} is too large.");
        return (int)length;
    }

    public static byte[] DecodePayload(byte[] remainder, ref uint iv)
    {
        if (iv == 0) return remainder;
        if (remainder.Length < 4) throw new InvalidDataException("Encrypted frame has no checksum.");
        uint currentIv = iv;
        int payloadLength = remainder.Length - 4;
        byte[] payload = remainder.AsSpan(0, payloadLength).ToArray();
        uint actualHash = KRPacketCrypto.HashDecrypt(payload, checked((uint)payload.Length), currentIv);
        uint wireChecksum = BinaryPrimitives.ReadUInt32LittleEndian(remainder.AsSpan(payloadLength));
        uint expected = currentIv ^ actualHash ^ ChecksumXor;
        if (wireChecksum != expected) throw new InvalidDataException("P236 frame checksum mismatch.");
        AdvanceIv(ref iv);
        return payload;
    }

    private static void AdvanceIv(ref uint iv)
    {
        iv = unchecked(iv + IvStep);
        if (iv == 0) iv = 1;
    }
}
