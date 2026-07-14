using KartRider;
using KartRider.Common.Utilities;
using KartRider.IO;

namespace KartRider.P236.Server.Tests;

public sealed class LoginProfileReaderTests
{
    [Fact]
    public void ReadsUsernameFromBoundedAccountProfile()
    {
        using OutPacket body = new();
        body.WriteUInt(Adler32Helper.GenerateAdler32_ASCII("AccountDataProfile"));
        body.WriteString("profile");
        body.WriteString(string.Empty);
        body.WriteInt(0);
        body.WriteInt(1);
        body.WriteString("username");
        body.WriteString("alice");
        body.WriteInt(0);
        body.WriteInt(0);
        using InPacket input = new(body.ToArray());
        Assert.Equal("alice", LegacyLoginProfileReader.ReadUsername(input));
        Assert.Equal(0, input.Available);
    }
}
