using KartRider;

namespace KartRider.P236.Server;

internal static class P236LicenseProgress
{
    public const byte MaximumLevel = 4;
    public const int CompletionMaskCount = 6;

    internal sealed class FloorSnapshot
    {
        private readonly ushort[] _completionMasks;

        private FloorSnapshot(bool enabled, byte licenseLevel, ushort[] completionMasks)
        {
            Enabled = enabled;
            LicenseLevel = licenseLevel;
            _completionMasks = completionMasks;
        }

        public bool Enabled { get; }

        public byte LicenseLevel { get; }

        public static FloorSnapshot Capture(P236ServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.DefaultLicenseLevel > MaximumLevel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    $"P236 license levels must be between 0 and {MaximumLevel} (L1).");
            }

            ushort[] completionMasks = options.DefaultLicenseCompletionMasks;
            if (completionMasks is null || completionMasks.Length != CompletionMaskCount)
            {
                throw new ArgumentException(
                    $"Exactly {CompletionMaskCount} default license completion masks are required.",
                    nameof(options));
            }

            return new FloorSnapshot(
                options.EnforceDefaultLicenseProgressFloor,
                options.DefaultLicenseLevel,
                (ushort[])completionMasks.Clone());
        }

        public bool Apply(LegacySessionProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (!Enabled)
            {
                return false;
            }

            bool changed = false;
            if (profile.LicenseLevel < LicenseLevel)
            {
                profile.LicenseLevel = LicenseLevel;
                changed = true;
            }

            ushort[] currentMasks = profile.GetLicenseCompletionMasks();
            bool masksChanged = false;
            for (int index = 0; index < currentMasks.Length; index++)
            {
                ushort merged = (ushort)(currentMasks[index] | _completionMasks[index]);
                if (merged == currentMasks[index])
                {
                    continue;
                }

                currentMasks[index] = merged;
                masksChanged = true;
            }

            if (masksChanged)
            {
                profile.SetLicenseCompletionMasks(currentMasks);
                changed = true;
            }

            return changed;
        }
    }

    public static ushort[] CreateReplayableL1MissionAccessMasks()
    {
        // Completion bits are indexed by the challenge id's high nibble. Mark
        // 0x41..0x44 complete so the imported linear require chain exposes
        // 0x45, but leave 0x45 itself clear so 0x46 remains locked.
        return [31, 7, 31, 63, 30, 0];
    }

    public static bool ApplyConfiguredProgressFloor(
        LegacySessionProfile profile,
        P236ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return FloorSnapshot.Capture(options).Apply(profile);
    }
}
