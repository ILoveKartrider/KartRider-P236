using System.Buffers.Binary;
using KartRider;

namespace KartRider.P236.Server.Tests;

public sealed class FrameCodecTests
{
    [Fact]
    public void ClearFrameRoundTrips()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        uint sendIv = 0;
        byte[] frame = P236FrameCodec.Encode(payload, ref sendIv);
        Assert.Equal(0u, sendIv);
        Assert.Equal(payload.Length, P236FrameCodec.DecodeRemainingLength(frame.AsSpan(0, 4), 0));
        uint receiveIv = 0;
        Assert.Equal(payload, P236FrameCodec.DecodePayload(frame[4..], ref receiveIv));
    }

    [Fact]
    public void EncryptedFrameRoundTripsAndAdvancesIv()
    {
        byte[] payload = Enumerable.Range(0, 97).Select(value => (byte)value).ToArray();
        const uint initialIv = 0xA5B6C7D8;
        uint sendIv = initialIv;
        byte[] frame = P236FrameCodec.Encode(payload, ref sendIv);
        int remainderLength = P236FrameCodec.DecodeRemainingLength(frame.AsSpan(0, 4), initialIv);
        Assert.Equal(payload.Length + 4, remainderLength);
        Assert.NotEqual(payload, frame.AsSpan(4, payload.Length).ToArray());
        uint receiveIv = initialIv;
        byte[] decoded = P236FrameCodec.DecodePayload(frame[4..], ref receiveIv);
        Assert.Equal(payload, decoded);
        Assert.Equal(sendIv, receiveIv);
        Assert.NotEqual(initialIv, receiveIv);
    }

    [Fact]
    public void EncryptedFrameRejectsBadChecksum()
    {
        uint iv = 1234;
        byte[] frame = P236FrameCodec.Encode([9, 8, 7], ref iv);
        frame[^1] ^= 0x40;
        uint receiveIv = 1234;
        Assert.Throws<InvalidDataException>(() => P236FrameCodec.DecodePayload(frame[4..], ref receiveIv));
    }
}
