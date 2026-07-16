using KartRider.P236.Connector;
using Xunit;

namespace KartRider.P236.Connector.Tests;

public sealed class LauncherHookOptionTests
{
    [Fact]
    public void HookOptionOnlyInvokesPatchActionWhenEnabled()
    {
        int applyCalls = 0;
        List<string> messages = [];
        InlineProgress progress = new InlineProgress(messages.Add);

        bool skipped = LauncherService.ApplyL1CompatibilityHooksIfEnabled(
            enabled: false,
            () => applyCalls++,
            progress);

        Assert.False(skipped);
        Assert.Equal(0, applyCalls);
        Assert.Contains(messages, message => message.Contains("적용 안 함", StringComparison.Ordinal));

        bool applied = LauncherService.ApplyL1CompatibilityHooksIfEnabled(
            enabled: true,
            () => applyCalls++,
            progress);

        Assert.True(applied);
        Assert.Equal(1, applyCalls);
    }

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
