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

    [Fact]
    public async Task ServerStartImmediatelyMigratesOnlyRequiredPersistedLicenseProgress()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        ServerRuntime.Configure(options);
        string path = System.IO.Path.Combine(options.DataDirectory, "profiles.json");
        JsonLegacyProfileStore store = new(path);
        LegacySessionProfile migratedProfile = LegacySessionProfile.CreateFromStaticTemplate(84);
        migratedProfile.SourceUsername = "existing-rider";
        migratedProfile.UserId = "existing-id";
        migratedProfile.Nickname = "Existing Rider";
        migratedProfile.RiderIntro = "preserve me";
        migratedProfile.RP = 234_567;
        migratedProfile.Lucci = 765_432;
        migratedProfile.Equipment.Character = 11;
        migratedProfile.Equipment.Kart = 29;
        migratedProfile.LicenseLevel = 3;
        migratedProfile.SetLicenseCompletionMasks([31, 7, 31, 63, 2, 0]);

        LegacySessionProfile unchangedProfile = LegacySessionProfile.CreateFromStaticTemplate(85);
        unchangedProfile.SourceUsername = "unchanged-rider";
        unchangedProfile.UserId = "unchanged-id";
        unchangedProfile.Nickname = "Unchanged Rider";
        unchangedProfile.RiderIntro = "leave me alone";
        unchangedProfile.RP = 345_678;
        unchangedProfile.Lucci = 876_543;
        unchangedProfile.Equipment.Character = 12;
        unchangedProfile.Equipment.Kart = 30;
        unchangedProfile.LicenseLevel = P236LicenseProgress.MaximumLevel;
        unchangedProfile.SetLicenseCompletionMasks([31, 7, 31, 63, 62, 8]);
        store.SaveAll(
        [
            LegacyProfileRecord.FromProfile(migratedProfile),
            LegacyProfileRecord.FromProfile(unchangedProfile)
        ]);

        await using (P236Server server = new())
        {
            await server.StartAsync(options);
            IReadOnlyList<LegacyProfileRecord> runningRecords =
                new JsonLegacyProfileStore(path).LoadAll();
            Assert.Equal(2, runningRecords.Count);

            LegacyProfileRecord migrated = Assert.Single(
                runningRecords,
                profile => profile.SourceUsername == "existing-rider");
            Assert.Equal("existing-id", migrated.UserId);
            Assert.Equal("Existing Rider", migrated.Nickname);
            Assert.Equal("preserve me", migrated.RiderIntro);
            Assert.Equal((uint)84, migrated.UserNo);
            Assert.Equal(234_567, migrated.RP);
            Assert.Equal((uint)765_432, migrated.Lucci);
            Assert.Equal((ushort)11, migrated.Equipment.Character);
            Assert.Equal((ushort)29, migrated.Equipment.Kart);
            Assert.Equal(P236LicenseProgress.MaximumLevel, migrated.LicenseLevel);
            Assert.Equal([31, 7, 31, 63, 30, 0], migrated.LicenseCompletionMasks);

            LegacyProfileRecord unchanged = Assert.Single(
                runningRecords,
                profile => profile.SourceUsername == "unchanged-rider");
            Assert.Equal("unchanged-id", unchanged.UserId);
            Assert.Equal("Unchanged Rider", unchanged.Nickname);
            Assert.Equal("leave me alone", unchanged.RiderIntro);
            Assert.Equal((uint)85, unchanged.UserNo);
            Assert.Equal(345_678, unchanged.RP);
            Assert.Equal((uint)876_543, unchanged.Lucci);
            Assert.Equal((ushort)12, unchanged.Equipment.Character);
            Assert.Equal((ushort)30, unchanged.Equipment.Kart);
            Assert.Equal(P236LicenseProgress.MaximumLevel, unchanged.LicenseLevel);
            Assert.Equal([31, 7, 31, 63, 62, 8], unchanged.LicenseCompletionMasks);
            await server.StopAsync();
        }
    }
}
