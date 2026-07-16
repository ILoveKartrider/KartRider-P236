#nullable disable
using KartRider.IO;
using KartRider.P236.Server;

namespace KartRider;

internal static class LegacyPacketHandlers
{
	private const int MaximumGameSlotDataLength = 960;

	public static void HandleStartSingle(SessionGroup session, InPacket packet)
	{
		RequireExactLength(packet, 25, "LoRqStartSinglePacket");
		uint startTick = packet.ReadUInt();
		LegacyIntegrityProof proof = ReadProof(packet);

		session.SingleRace.IsActive = true;
		session.SingleRace.StartTick = startTick;
		session.SingleRace.StartProof = proof;
		session.TimeAttackStartTicks = startTick;
		session.PlaneCheck1 = (byte)startTick;
		// This value was diagnostic-only in the recovered server and is not part
		// of any response. Keep a bounded deterministic counter without importing
		// the unrelated legacy lookup tables.
		session.SendPlaneCount = (int)(startTick % 5 + 6);
		session.TotalSendPlaneCount = 0;
	}

	public static void HandleGameReport(SessionGroup session, InPacket packet)
	{
		RequireExactLength(packet, 8, "GameReportPacket");
		session.SingleRace.LastReportValue1 = packet.ReadUInt();
		session.SingleRace.LastReportValue2 = packet.ReadUInt();
	}

	public static void HandleGameSlot(SessionGroup session, InPacket packet)
	{
		if (packet.Available < 13)
		{
			throw InvalidLength("GameSlotPacket", packet.Available, "at least 13");
		}

		uint value1 = packet.ReadUInt();
		uint value2 = packet.ReadUInt();
		bool hasExtendedValues = packet.ReadByte() != 0;
		uint[] extendedValues = new uint[3];
		if (hasExtendedValues)
		{
			if (packet.Available < 16)
			{
				throw InvalidLength("GameSlotPacket", packet.Available, "at least 16 after the extended-value flag");
			}

			for (int i = 0; i < extendedValues.Length; i++)
			{
				extendedValues[i] = packet.ReadUInt();
			}
		}

		int dataLength = packet.ReadInt();
		if (dataLength < 0 || dataLength > MaximumGameSlotDataLength || dataLength != packet.Available)
		{
			throw InvalidLength("GameSlotPacket data", packet.Available, $"exactly {dataLength} (0..{MaximumGameSlotDataLength})");
		}

		session.SingleRace.LastSlotValue1 = value1;
		session.SingleRace.LastSlotValue2 = value2;
		session.SingleRace.LastSlotHasExtendedValues = hasExtendedValues;
		session.SingleRace.LastSlotExtendedValues = extendedValues;
		session.SingleRace.LastSlotData = packet.ReadBytes(dataLength);
	}

	public static void HandleAddRacingTime(SessionGroup session, InPacket packet)
	{
		RequireExactLength(packet, 30, "LoRqAddRacingTimePacket");
		session.SingleRace.LastRacingTimeValue = packet.ReadUInt();
		session.SingleRace.LastRacingTimeMode = packet.ReadByte();
		session.SingleRace.LastRacingTimeStartTick = packet.ReadUInt();
		session.SingleRace.LastRacingTimeProof = ReadProof(packet);
		session.SingleRace.IsActive = false;
	}

	public static void HandleUpdateCompletion(SessionGroup session, InPacket packet)
	{
		RequireExactLength(packet, 17, "LoRqUpdateCbPacket");
		byte updatedRow = packet.ReadByte();
		if (updatedRow != byte.MaxValue && updatedRow > P236LicenseProgress.MaximumLevel)
		{
			throw new PacketReadException(
				$"LoRqUpdateCbPacket row {updatedRow} is not supported; " +
				$"this build supports levels 0..{P236LicenseProgress.MaximumLevel} (L1).");
		}
		ushort[] completionMasks = new ushort[P236LicenseProgress.CompletionMaskCount];
		for (int i = 0; i < completionMasks.Length; i++)
		{
			completionMasks[i] = packet.ReadUShort();
		}

		uint rewardId = packet.ReadUInt();
		RouterListener.UpdateLicenseProgress(session, updatedRow, completionMasks);
		session.SingleRace.LastCompletionRow = updatedRow;
		session.SingleRace.LastCompletionRewardId = rewardId;
		session.SingleRace.IsActive = false;
	}

	private static LegacyIntegrityProof ReadProof(InPacket packet)
	{
		LegacyIntegrityProof proof = new LegacyIntegrityProof
		{
			Seed = packet.ReadUInt(),
			Accumulator = packet.ReadUInt(),
			CheckByte = packet.ReadByte()
		};
		for (int i = 0; i < proof.CheckValues.Length; i++)
		{
			proof.CheckValues[i] = packet.ReadUInt();
		}
		return proof;
	}

	private static void RequireExactLength(InPacket packet, int expected, string packetName)
	{
		if (packet.Available != expected)
		{
			throw InvalidLength(packetName, packet.Available, expected.ToString());
		}
	}

	private static PacketReadException InvalidLength(string packetName, int actual, string expected)
	{
		return new PacketReadException($"{packetName} body length was {actual}; expected {expected} bytes.");
	}
}
