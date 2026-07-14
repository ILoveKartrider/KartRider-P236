#nullable disable
namespace KartRider;

internal sealed class LegacyIntegrityProof
{
	public uint Seed { get; set; }

	public uint Accumulator { get; set; }

	public byte CheckByte { get; set; }

	public uint[] CheckValues { get; set; } = new uint[3];
}

internal sealed class LegacySingleRaceState
{
	public bool IsActive { get; set; }

	public uint StartTick { get; set; }

	public LegacyIntegrityProof StartProof { get; set; } = new LegacyIntegrityProof();

	public uint LastReportValue1 { get; set; }

	public uint LastReportValue2 { get; set; }

	public uint LastSlotValue1 { get; set; }

	public uint LastSlotValue2 { get; set; }

	public bool LastSlotHasExtendedValues { get; set; }

	public uint[] LastSlotExtendedValues { get; set; } = new uint[3];

	public byte[] LastSlotData { get; set; } = new byte[0];

	public uint LastRacingTimeValue { get; set; }

	public byte LastRacingTimeMode { get; set; }

	public uint LastRacingTimeStartTick { get; set; }

	public LegacyIntegrityProof LastRacingTimeProof { get; set; } = new LegacyIntegrityProof();

	public byte LastCompletionRow { get; set; }

	public uint LastCompletionRewardId { get; set; }
}
