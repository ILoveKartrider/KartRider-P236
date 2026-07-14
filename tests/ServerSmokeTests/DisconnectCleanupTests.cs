using KartRider;

namespace KartRider.P236.Server.Tests;

public sealed class DisconnectCleanupTests
{
    [Fact]
    public void SessionRemovalRunsWhenRoomCleanupThrows()
    {
        int removalCount = 0;

        Exception? escaped = Record.Exception(() => ClientConnection.RunDisconnectCleanup(
            () => throw new InvalidOperationException("synthetic room cleanup failure"),
            () => removalCount++));

        Assert.Null(escaped);
        Assert.Equal(1, removalCount);
    }

    [Fact]
    public void CleanupRemainsNonThrowingWhenBothCallbacksFail()
    {
        Exception? escaped = Record.Exception(() => ClientConnection.RunDisconnectCleanup(
            () => throw new InvalidOperationException("synthetic room cleanup failure"),
            () => throw new IOException("synthetic session removal failure")));

        Assert.Null(escaped);
    }
}
