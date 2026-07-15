using KartRider;

namespace KartRider.P236.Server.Tests;

public sealed class ProfileStoreTests
{
    [Fact]
    public void JsonProfileSurvivesStoreReopen()
    {
        using TemporaryDirectory temporary = new();
        ServerRuntime.Configure(TestOptions.Create(temporary.Path));
        string path = System.IO.Path.Combine(temporary.Path, "profiles.json");
        JsonLegacyProfileStore first = new(path);
        LegacySessionProfile profile = LegacySessionProfile.CreateFromStaticTemplate(42);
        profile.SourceUsername = "alice";
        profile.UserId = "alice";
        profile.Nickname = "Alice";
        profile.Equipment.Kart = 17;
        profile.LicenseLevel = 4;
        profile.SetLicenseCompletionMasks([31, 7, 31, 63, 21, 0]);
        first.Save(LegacyProfileRecord.FromProfile(profile));

        LegacyProfileRecord loaded = Assert.Single(new JsonLegacyProfileStore(path).LoadAll());
        Assert.Equal("alice", loaded.SourceUsername);
        Assert.Equal((uint)42, loaded.UserNo);
        Assert.Equal((ushort)17, loaded.Equipment.Kart);
        Assert.Equal(4, loaded.LicenseLevel);
        Assert.Equal([31, 7, 31, 63, 21, 0], loaded.LicenseCompletionMasks);
    }
}
