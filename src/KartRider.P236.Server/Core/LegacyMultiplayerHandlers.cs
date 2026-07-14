#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using KartRider.Common.Utilities;
using KartRider.IO;
using KartRider.P236.Server;

namespace KartRider;

internal sealed class LegacyMultiplayerState
{
	public byte Channel { get; set; }

	public ushort ChannelToken { get; set; }

	public uint UserNo { get; set; }

	public IPEndPoint ReportedUdpEndPoint { get; set; }

	public IPEndPoint ObservedUdpEndPoint { get; set; }

	public int RoomId { get; set; } = -1;

	public ushort EquipmentReserved0 { get; set; }

	public ushort EquipmentReserved1 { get; set; }
}

internal static class LegacyChannelPolicy
{
	// P236 resolves its driving preset locally from the selected channel group.
	// These values match the client mapper used while entering the race stage.
	public static bool TryGetSpeedType(byte channel, out byte speedType)
	{
		switch (channel)
		{
			case 1: // novice
				speedType = 0;
				return true;
			case 2: // rookieIntro
			case 3: // rookie
			case 7: // event (aliases rookie)
				speedType = 1;
				return true;
			case 4: // L3
			case 6: // arena (aliases L3)
				speedType = 2;
				return true;
			case 5: // L2
				speedType = 3;
				return true;
			default:
				speedType = 0;
				return false;
		}
	}
}

internal sealed class LegacyRoomMember
{
	public LegacyRoomMember(
		SessionGroup session,
		int slotId,
		int status,
		bool initialized,
		bool isObserver = false)
	{
		Session = session;
		SlotId = slotId;
		Status = status;
		Initialized = initialized;
		IsObserver = isObserver;
		GridPosition = checked((byte)slotId);
	}

	public SessionGroup Session { get; }

	public int SlotId { get; }

	public int Status { get; set; }

	public bool Initialized { get; set; }

	public bool IsObserver { get; }

	public bool LoadedInGame { get; set; }

	public byte Team { get; set; }

	public byte GridPosition { get; set; }

	public long JoinSequence { get; set; }

	public bool FinishedInGame { get; set; }

	public uint FinishTime { get; set; } = uint.MaxValue;

	public uint FlagScore { get; set; }

	public uint[] ItemVector { get; set; } = Array.Empty<uint>();
}

internal sealed class LegacyRaceParticipantSnapshot
{
	public int SlotId { get; init; }

	public byte Team { get; init; }

	public byte GridPosition { get; init; }

	public bool Finished { get; set; }

	public uint FinishTime { get; set; } = uint.MaxValue;

	public uint FlagScore { get; set; }
}

internal sealed class LegacyRoom
{
	public object SyncRoot { get; } = new object();

	public int Id { get; init; }

	public string Name { get; init; } = string.Empty;

	public string Password { get; init; } = string.Empty;

	public byte GameType { get; init; }

	public byte Channel { get; init; }

	public byte SpeedType { get; init; }

	public int Unknown1 { get; init; }

	public int Unknown2 { get; init; }

	public byte[] TrackCandidates { get; set; } = Array.Empty<byte>();

	public byte[] IntegrityProof { get; init; } = Array.Empty<byte>();

	public uint Track { get; set; }

	public LegacyRoomMember[] Members { get; } = new LegacyRoomMember[16];

	public int OwnerSlot { get; set; }

	public bool Started { get; set; }

	public int[] RaceGridSlots { get; set; } = Array.Empty<int>();

	public LegacyRaceParticipantSnapshot[] RaceParticipants { get; set; } =
		Array.Empty<LegacyRaceParticipantSnapshot>();

	public float RedTeamBoosterGauge { get; set; }

	public float BlueTeamBoosterGauge { get; set; }

	public Dictionary<uint, int> FlagOwnerSlots { get; } = new Dictionary<uint, int>();

	public uint RedFlagScore { get; set; }

	public uint BlueFlagScore { get; set; }

	public bool FlagTeamOvertime { get; set; }

	public uint FlagTeamOvertimeStartTick { get; set; }

	public bool StartSignalSent { get; set; }

	public uint StartTick { get; set; }

	public long NextJoinSequence { get; set; }

	public int RaceGeneration { get; set; }

	public bool RaceEnding { get; set; }

	public uint RaceEndTick { get; set; }
}

internal sealed class LegacyRoomListEntry
{
	public int Id { get; init; }

	public string Name { get; init; } = string.Empty;

	public bool Locked { get; init; }

	public byte GameType { get; init; }

	public uint Track { get; init; }

	public bool Started { get; init; }

	public int Occupants { get; init; }
}

internal sealed class LegacyRaceResultEntry
{
	public int SlotId { get; init; }

	public uint FinishTime { get; init; }

	public int Rank { get; init; }

	public byte Team { get; init; }

	public int Points { get; init; }
}

internal static class LegacyRoomManager
{
	private static readonly object SyncRoot = new object();
	private static readonly Dictionary<int, LegacyRoom> Rooms = new Dictionary<int, LegacyRoom>();
	private static int _nextRoomId = 1;

	public static LegacyRoom Create(
		SessionGroup owner,
		string name,
		string password,
		byte gameType,
		byte channel,
		byte speedType,
		int unknown1,
		int unknown2,
		byte[] trackCandidates,
		byte[] integrityProof)
	{
		lock (SyncRoot)
		{
			RemoveSessionLocked(owner);
			bool isObserver = LegacyObserverPolicy.IsObserver(owner.Profile);
			LegacyRoom room = new LegacyRoom
			{
				Id = _nextRoomId++,
				Name = name,
				Password = password,
				GameType = gameType,
				Channel = channel,
				SpeedType = speedType,
				Unknown1 = unknown1,
				Unknown2 = unknown2,
				TrackCandidates = trackCandidates,
				IntegrityProof = integrityProof,
				Track = 0,
				OwnerSlot = -1,
				NextJoinSequence = 1
			};
			int ownerSlot = isObserver
				? FindAvailableObserverSlotLocked(room)
				: FindAvailableSlotLocked(room);
			if (ownerSlot < 0)
			{
				throw new InvalidOperationException("The new room has no slot for its owner.");
			}

			room.OwnerSlot = ownerSlot;
			room.Members[ownerSlot] = new LegacyRoomMember(
				owner,
				ownerSlot,
				isObserver ? 4 : 2,
				initialized: false,
				isObserver)
			{
				Team = !isObserver && IsTeamGameType(gameType) ? (byte)2 : (byte)0,
				GridPosition = 0,
				JoinSequence = 0
			};
			Rooms.Add(room.Id, room);
			owner.Multiplayer.RoomId = room.Id;
			return room;
		}
	}

	public static bool TryJoin(
		SessionGroup session,
		int roomId,
		string password,
		out LegacyRoom room,
		out LegacyRoomMember member)
	{
		lock (SyncRoot)
		{
			member = null;
			if (!Rooms.TryGetValue(roomId, out room))
			{
				room = null;
				return false;
			}

			lock (room.SyncRoot)
			{
				if (room.Channel != session.Multiplayer.Channel)
				{
					room = null;
					return false;
				}

				LegacyRoomMember existing = FindMemberLocked(room, session);
				if (existing != null)
				{
					member = existing;
					return true;
				}

				if (room.Started || !string.Equals(room.Password, password, StringComparison.Ordinal))
				{
					room = null;
					return false;
				}

				bool isObserver = LegacyObserverPolicy.IsObserver(session.Profile);
				int slotId = isObserver
					? FindAvailableObserverSlotLocked(room)
					: FindAvailableSlotLocked(room);
				if (slotId < 0)
				{
					room = null;
					return false;
				}

				RemoveSessionLocked(session);
				member = new LegacyRoomMember(
					session,
					slotId,
					isObserver ? 4 : 2,
					initialized: false,
					isObserver)
				{
					Team = isObserver ? (byte)0 : SelectJoinTeamLocked(room),
					GridPosition = isObserver
						? (byte)0
						: checked((byte)CountMembersLocked(room)),
					JoinSequence = room.NextJoinSequence++
				};
				room.Members[slotId] = member;
				if (isObserver)
				{
					// The original observer role is authoritative: entering an
					// existing room takes room-master control without turning the
					// observer into a racer. The displaced rider remains status 2
					// and must ready like every other racer before the observer starts.
					room.OwnerSlot = slotId;
				}
				session.Multiplayer.RoomId = room.Id;
				return true;
			}
		}
	}

	public static LegacyRoom GetFor(SessionGroup session)
	{
		lock (SyncRoot)
		{
			if (!Rooms.TryGetValue(session.Multiplayer.RoomId, out LegacyRoom room))
			{
				return null;
			}

			lock (room.SyncRoot)
			{
				return FindMemberLocked(room, session) != null ? room : null;
			}
		}
	}

	public static IReadOnlyList<LegacyRoomListEntry> GetList(byte channel)
	{
		lock (SyncRoot)
		{
			List<LegacyRoomListEntry> result = new List<LegacyRoomListEntry>();
			foreach (LegacyRoom room in Rooms.Values)
			{
				lock (room.SyncRoot)
				{
					if (room.Channel != channel)
					{
						continue;
					}

					result.Add(new LegacyRoomListEntry
					{
						Id = room.Id,
						Name = room.Name,
						Locked = !string.IsNullOrEmpty(room.Password),
						GameType = room.GameType,
						Track = room.Track,
						Started = room.Started,
						Occupants = CountMembersLocked(room)
					});
				}
			}

			result.Sort((left, right) => left.Id.CompareTo(right.Id));
			return result;
		}
	}

	public static int Count
	{
		get
		{
			lock (SyncRoot)
			{
				return Rooms.Count;
			}
		}
	}

	public static int CountMembers(LegacyRoom room)
	{
		lock (room.SyncRoot)
		{
			return CountMembersLocked(room);
		}
	}

	public static int CountInitializedRiders(LegacyRoom room)
	{
		lock (room.SyncRoot)
		{
			int count = 0;
			foreach (LegacyRoomMember member in room.Members)
			{
				if (member != null && !member.IsObserver && member.Initialized &&
					(member.Status == 2 || member.Status == 3 || member.Status == 5))
				{
					count++;
				}
			}

			return count;
		}
	}

	public static LegacyRoom Remove(SessionGroup session)
	{
		lock (SyncRoot)
		{
			return RemoveSessionLocked(session);
		}
	}

	public static LegacyRoom RemoveSession(SessionGroup session) => Remove(session);

	private static LegacyRoom RemoveSessionLocked(SessionGroup session)
	{
		int roomId = session.Multiplayer.RoomId;
		session.Multiplayer.RoomId = -1;
		if (roomId < 0 || !Rooms.TryGetValue(roomId, out LegacyRoom room))
		{
			return null;
		}

		lock (room.SyncRoot)
		{
			LegacyRoomMember member = FindMemberLocked(room, session);
			if (member == null)
			{
				return null;
			}

			room.Members[member.SlotId] = null;
			if (CountAllMembersLocked(room) == 0)
			{
				Rooms.Remove(roomId);
				return room;
			}

			if (!room.Started)
			{
				NormalizeGridPositionsLocked(room);
				RebalanceTeamsLocked(room);
			}

			if (member.SlotId == room.OwnerSlot)
			{
				room.OwnerSlot = FindFirstMemberSlotLocked(room);
				LegacyRoomMember newOwner = room.Members[room.OwnerSlot];
				if (newOwner != null && !newOwner.IsObserver)
				{
					newOwner.Status = 2;
				}
			}

			return room;
		}
	}

	private static LegacyRoomMember FindMemberLocked(LegacyRoom room, SessionGroup session)
	{
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member != null && ReferenceEquals(member.Session, session))
			{
				return member;
			}
		}

		return null;
	}

	private static int FindAvailableSlotLocked(LegacyRoom room, int firstSlot = 0)
	{
		// The wire format has 16 slot records, but only slots 0-7 admit riders;
		// slots 8-15 are reserved for compact observer records.
		int maximumRiderSlots = Math.Min(8, room.Members.Length);
		for (int slot = firstSlot; slot < maximumRiderSlots; slot++)
		{
			if (room.Members[slot] == null)
			{
				return slot;
			}
		}

		return -1;
	}

	private static int FindAvailableObserverSlotLocked(LegacyRoom room)
	{
		// P236 appends eight compact observer records after the eight full rider
		// records. Their logical room IDs are therefore 8-15.
		int firstObserverSlot = Math.Min(8, room.Members.Length);
		for (int slot = firstObserverSlot; slot < room.Members.Length; slot++)
		{
			if (room.Members[slot] == null)
			{
				return slot;
			}
		}

		return -1;
	}

	private static int FindFirstMemberSlotLocked(LegacyRoom room)
	{
		LegacyRoomMember first = null;
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member == null)
			{
				continue;
			}

			if (first == null ||
				(first.IsObserver && !member.IsObserver) ||
				(first.IsObserver == member.IsObserver &&
					(member.GridPosition < first.GridPosition ||
						(member.GridPosition == first.GridPosition &&
							(member.JoinSequence < first.JoinSequence ||
								(member.JoinSequence == first.JoinSequence &&
									member.SlotId < first.SlotId))))))
			{
				first = member;
			}
		}

		// After a race, grid order is finish order. Selecting the leading grid
		// member therefore hands an exiting winner's authority to the runner-up.
		return first?.SlotId ?? 0;
	}

	private static int CountMembersLocked(LegacyRoom room)
	{
		int count = 0;
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member != null && !member.IsObserver)
			{
				count++;
			}
		}

		return count;
	}

	private static int CountAllMembersLocked(LegacyRoom room)
	{
		int count = 0;
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member != null)
			{
				count++;
			}
		}

		return count;
	}

	private static bool IsTeamGameType(byte gameType)
	{
		// P236 aliases rider-school speed/item rooms (5/6) to the normal team
		// stages (3/4). Flag Team is 7; Flag Individual is 8.
		return gameType == 3 || gameType == 4 || gameType == 5 || gameType == 6 ||
			gameType == 7;
	}

	private static byte SelectJoinTeamLocked(LegacyRoom room)
	{
		if (!IsTeamGameType(room.GameType))
		{
			return 0;
		}

		CountTeamsLocked(room, out int redCount, out int blueCount);
		// The 2005 client uses 1 for red and 2 for blue. The creator starts on
		// blue, so choosing blue on a tie produces B/R/B/R while still keeping
		// both four-rider teams within one member of each other.
		return blueCount <= redCount ? (byte)2 : (byte)1;
	}

	private static void CountTeamsLocked(LegacyRoom room, out int redCount, out int blueCount)
	{
		redCount = 0;
		blueCount = 0;
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member?.Team == 1)
			{
				redCount++;
			}
			else if (member?.Team == 2)
			{
				blueCount++;
			}
		}
	}

	private static void RebalanceTeamsLocked(LegacyRoom room)
	{
		if (!IsTeamGameType(room.GameType))
		{
			return;
		}

		CountTeamsLocked(room, out int redCount, out int blueCount);
		while (Math.Abs(redCount - blueCount) > 1)
		{
			byte crowdedTeam = redCount > blueCount ? (byte)1 : (byte)2;
			byte sparseTeam = crowdedTeam == 1 ? (byte)2 : (byte)1;
			LegacyRoomMember candidate = null;
			foreach (LegacyRoomMember member in room.Members)
			{
				if (member == null || member.IsObserver || member.Team != crowdedTeam)
				{
					continue;
				}

				// Preserve the room master's team whenever another rider can restore
				// balance. Among guests, move the most recent entrant.
				if (candidate == null ||
					(candidate.SlotId == room.OwnerSlot && member.SlotId != room.OwnerSlot) ||
					(candidate.SlotId != room.OwnerSlot && member.SlotId != room.OwnerSlot &&
						member.JoinSequence > candidate.JoinSequence))
				{
					candidate = member;
				}
			}

			if (candidate == null)
			{
				break;
			}

			candidate.Team = sparseTeam;
			// Moving a rider between teams invalidates their previous ready state.
			candidate.Status = 2;
			if (crowdedTeam == 1)
			{
				redCount--;
				blueCount++;
			}
			else
			{
				blueCount--;
				redCount++;
			}
		}
	}

	private static void NormalizeGridPositionsLocked(LegacyRoom room)
	{
		List<LegacyRoomMember> members = new List<LegacyRoomMember>();
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member != null && !member.IsObserver)
			{
				members.Add(member);
			}
		}

		members.Sort((left, right) =>
		{
			int byGrid = left.GridPosition.CompareTo(right.GridPosition);
			if (byGrid != 0)
			{
				return byGrid;
			}

			int byJoin = left.JoinSequence.CompareTo(right.JoinSequence);
			return byJoin != 0 ? byJoin : left.SlotId.CompareTo(right.SlotId);
		});
		for (int position = 0; position < members.Count; position++)
		{
			members[position].GridPosition = checked((byte)position);
		}
	}

}

internal static class LegacyPacketTrace
{
	private const int MaximumDumpBytes = 192;
	private static readonly object SyncRoot = new object();
	private static readonly IReadOnlyDictionary<uint, string> SupplementalPacketNames =
		new Dictionary<uint, string>
		{
			[0x402306ECu] = "LoRqGetRiderPacket",
			[0x401406EBu] = "LoRpGetRiderPacket",
			[0x7DE309D3u] = "LoRqEventTempRewardPacket",
			[0x7DCD09D2u] = "LoRpEventTempRewardPacket",
			[0x7C4409D2u] = "ChSecedeRoomRequestPacket",
			[0x689508F5u] = "ChSecedeRoomReplyPacket",
			[0x53410808u] = "GrRequestStartPacket",
			[0x42F1072Bu] = "GrReplyStartPacket",
			[0x50EC07DEu] = "GrCommandStartPacket"
		};
	private static string TracePath => Path.Combine(
		ServerRuntime.Options.LogDirectory,
		"p236-packets.log");

	static LegacyPacketTrace()
	{
		if (!ServerRuntime.Options.EnablePacketTrace)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(TracePath)!);
			File.WriteAllText(
				TracePath,
				$"Korean 2005 packet trace started {DateTimeOffset.Now:O}{Environment.NewLine}");
		}
		catch
		{
			// Packet handling must not fail because diagnostics are unavailable.
		}
	}

	public static void LogReceive(uint hash, InPacket packet)
	{
		if (!ServerRuntime.Options.EnablePacketTrace)
		{
			return;
		}
		byte[] frame = packet.ToArray();
		LogFrame("RX", hash, frame);
	}

	public static void LogSend(byte[] frame)
	{
		if (!ServerRuntime.Options.EnablePacketTrace)
		{
			return;
		}
		if (frame == null || frame.Length < sizeof(uint))
		{
			LogEvent($"[2005 TX] malformed frame length={frame?.Length ?? 0}.");
			return;
		}

		LogFrame("TX", BitConverter.ToUInt32(frame, 0), frame);
	}

	private static void LogFrame(string direction, uint hash, byte[] frame)
	{
		string packetName = SupplementalPacketNames.TryGetValue(hash, out string name) ? name : "unknown";
		int bodyLength = Math.Max(0, frame.Length - sizeof(uint));
		int dumpLength = Math.Min(frame.Length, MaximumDumpBytes);
		string suffix = frame.Length > dumpLength ? " ..." : string.Empty;
		LogEvent(
			$"[2005 {direction}] {packetName} hash=0x{hash:X8} body={bodyLength} " +
			$"hex={BitConverter.ToString(frame, 0, dumpLength).Replace('-', ' ')}{suffix}");
	}

	public static void LogEvent(string message)
	{
		Console.WriteLine(message);
		if (!ServerRuntime.Options.EnablePacketTrace)
		{
			return;
		}
		try
		{
			lock (SyncRoot)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(TracePath)!);
				File.AppendAllText(
					TracePath,
					$"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
			}
		}
		catch
		{
			// Keep the compatibility server functional if the log cannot be written.
		}
	}
}

internal static class LegacyMultiplayerHandlers
{
	private const uint RaceStartCutsceneMilliseconds = 4000u;
	private const uint RaceCountdownMilliseconds = 3000u;
	private const uint FlagOvertimeTransitionPaddingMilliseconds = 2000u;
	private const uint FlagRaceMilliseconds = 180000u;

	private static readonly uint PqChannelSwitch = Hash("PqChannelSwitch");
	private static readonly uint PqChannelMovein = Hash("PqChannelMovein");
	private static readonly uint ChGetCurrentGpRequest = Hash("ChGetCurrentGpRequestPacket");
	private static readonly uint ChGetRoomListRequest = Hash("ChGetRoomListRequestPacket");
	private static readonly uint ChCreateRoomRequest = Hash("ChCreateRoomRequestPacket");
	private static readonly uint ChJoinRoomRequest = Hash("ChJoinRoomRequestPacket");
	private static readonly uint ChSecedeRoomRequest = Hash("ChSecedeRoomRequestPacket");
	private static readonly uint ChClientUdpAddr = Hash("ChClientUdpAddrPacket");
	private static readonly uint GrFirstRequest = Hash("GrFirstRequestPacket");
	private static readonly uint GrChangeTeam = Hash("GrChangeTeamPacket");
	private static readonly uint GrChangeTrack = Hash("GrChangeTrackPacket");
	private static readonly uint GrRequestSetSlotState = Hash("GrRequestSetSlotStatePacket");
	private static readonly uint GrRequestStart = Hash("GrRequestStartPacket");
	private static readonly uint GameTeamBoosterRequestAddGauge =
		Hash("GameTeamBoosterRequestAddGaugePacket");
	private static readonly uint GameControl = Hash("GameControlPacket");
	private static readonly uint GameSlot = Hash("GameSlotPacket");
	private static readonly uint GameFlagRequestAddScore = Hash("GameFlagRequestAddScorePacket");
	private static readonly uint GameFlagGameScore = Hash("GameFlagGameScorePacket");
	private static readonly uint GopCube = Hash("GopCube");
	private static readonly uint GoItemCube = Hash("GoItemCube");
	private static readonly uint GopTeamFlag = Hash("GopTeamFlag");
	private static readonly uint GoTeamFlag = Hash("GoTeamFlag");
	private static readonly uint GameKartItemInfo = Hash("GameKartItemInfoPacket");
	private static readonly Dictionary<uint, uint> P236RelayedItemOperations =
		new Dictionary<uint, uint>
		{
			[Hash("GopCloud")] = Hash("GoItemCloud"),
			[Hash("GopBanana")] = Hash("GoItemBanana"),
			[Hash("GopShield")] = Hash("GoItemShield"),
			[Hash("GopSiren")] = Hash("GoItemSiren"),
			[Hash("GopSirenShield")] = Hash("GoItemSirenShield"),
			[Hash("GopAreaUfo")] = Hash("GoItemAreaUfo"),
			[Hash("GopForceZone")] = Hash("GoItemForceZone"),
			[Hash("GopRocket")] = Hash("GoItemRocket"),
			[Hash("GopWaterfly")] = Hash("GoItemWaterfly"),
			[Hash("GopMagnet")] = Hash("GoItemMagnet"),
			[Hash("GopWaterbomb")] = Hash("GoItemWaterbomb"),
			[Hash("GopUfo")] = Hash("GoItemUfo"),
			[Hash("GopDevil")] = Hash("GoItemDevil"),
			[Hash("GopAngel")] = Hash("GoItemAngel"),
			[Hash("GopEmp")] = Hash("GoItemEmp"),
			[Hash("GopTimebomb")] = Hash("GoItemTimebomb"),
			// P236 flag-mode item tables add these three operations. They use the
			// same masked, non-extended GameSlot envelope as the common items.
			[Hash("GopGhost")] = Hash("GoItemGhost"),
			[Hash("GopMine")] = Hash("GoItemMine"),
			[Hash("GopRollingbomb")] = Hash("GoItemRollingbomb"),
			// Equipment reactions are emitted through the same P236 item-operation
			// slot when they absorb or modify an incoming item.
			[Hash("GopBalloon")] = Hash("GoItemBalloon"),
			[Hash("GopHeadBand")] = Hash("GoItemHeadBand")
		};
	private static readonly uint LoRqSetRiderItemOn = Hash("LoRqSetRiderItemOnPacket");
	private static readonly uint LoRqEventTempReward = Hash("LoRqEventTempRewardPacket");

	public static void HandleDisconnect(SessionGroup session)
	{
		LegacyRoom previousRoom = LegacyRoomManager.RemoveSession(session);
		if (previousRoom == null)
		{
			return;
		}

		List<SessionGroup> startTargets = null;
		uint startTick = 0;
		int loadedCount = 0;
		int participantCount = 0;
		int raceGeneration = 0;
		bool startsFlagTeamRace = false;
		bool raceStarted;
		lock (previousRoom.SyncRoot)
		{
			raceStarted = previousRoom.Started;
			if (raceStarted)
			{
				TryPrepareGameStartSignalLocked(
					previousRoom,
					out startTargets,
					out startTick,
					out loadedCount,
					out participantCount);
				if (startTargets != null)
				{
					raceGeneration = previousRoom.RaceGeneration;
					startsFlagTeamRace = previousRoom.GameType == 7;
				}
			}
		}

		if (!raceStarted)
		{
			BroadcastSlotData(previousRoom);
			return;
		}

		if (startTargets != null)
		{
			BroadcastGameStartSignal(previousRoom.Id, startTargets, startTick);
			if (startsFlagTeamRace)
			{
				BroadcastFlagTeamScore(startTargets, 0, 0);
				_ = EndFlagTeamRaceAtDeadlineAsync(previousRoom, raceGeneration, startTick);
			}
			LegacyPacketTrace.LogEvent(
				$"[2005 GAME] Released loading wait after disconnect in room={previousRoom.Id}; " +
				$"loaded={loadedCount}/{participantCount}, startTick={startTick}, targets={startTargets.Count}.");
		}
	}

	public static bool TryHandle(SessionGroup session, uint hash, InPacket packet)
	{
		if (hash == PqChannelSwitch)
		{
			HandleChannelSwitch(session, packet);
			return true;
		}

		if (hash == PqChannelMovein)
		{
			HandleChannelMoveIn(session, packet);
			return true;
		}

		if (hash == ChGetCurrentGpRequest)
		{
			HandleCurrentGp(session);
			return true;
		}

		if (hash == ChGetRoomListRequest)
		{
			HandleRoomList(session, packet);
			return true;
		}

		if (hash == ChClientUdpAddr)
		{
			HandleClientUdpAddress(session, packet);
			return true;
		}

		if (hash == ChCreateRoomRequest)
		{
			HandleCreateRoom(session, packet);
			return true;
		}

		if (hash == ChJoinRoomRequest)
		{
			HandleJoinRoom(session, packet);
			return true;
		}

		if (hash == ChSecedeRoomRequest)
		{
			HandleSecedeRoom(session, packet);
			return true;
		}

		if (hash == GrFirstRequest)
		{
			HandleFirstRoomRequest(session);
			return true;
		}

		if (hash == GrRequestSetSlotState)
		{
			HandleSetSlotState(session, packet);
			return true;
		}

		if (hash == GrChangeTeam)
		{
			HandleChangeTeam(session, packet);
			return true;
		}

		if (hash == GrChangeTrack)
		{
			HandleChangeTrack(session, packet);
			return true;
		}

		if (hash == GrRequestStart)
		{
			HandleStartRoom(session, packet);
			return true;
		}

		if (hash == GameTeamBoosterRequestAddGauge)
		{
			HandleTeamBoosterGauge(session, packet);
			return true;
		}

		if (hash == GameFlagRequestAddScore)
		{
			HandleFlagTeamScoreRequest(session, packet);
			return true;
		}

		if (hash == GameControl)
		{
			HandleGameControl(session, packet);
			return true;
		}

		if (hash == GameSlot && TryHandleGameSlot(session, packet))
		{
			return true;
		}

		if (hash == LoRqSetRiderItemOn)
		{
			HandleSetRiderItemOn(session, packet);
			return true;
		}

		if (hash == LoRqEventTempReward)
		{
			HandleEventTempReward(session, packet);
			return true;
		}

		return false;
	}

	private static bool TryHandleGameSlot(SessionGroup session, InPacket packet)
	{
		byte[] wirePacket = packet.ToArray();
		if (!TryParseGameSlotEnvelope(
			wirePacket,
			out uint outerRider,
			out uint recipientMask,
			out bool hasExtendedValues,
			out uint[] extendedValues,
			out int rawOffset,
			out int rawLength,
			out uint rawType))
		{
			return false;
		}

		// P236 does not receive an item ID from the server when a rider crosses an
		// item cube. The authoritative server echoes GopCube to every racer,
		// including its sender. The local GoItemSlot then performs the item draw.
		if (rawType == GopCube && hasExtendedValues && recipientMask == uint.MaxValue &&
			rawLength == 24 &&
			BitConverter.ToUInt32(wirePacket, rawOffset + 4) == GoItemCube &&
			BitConverter.ToUInt32(wirePacket, rawOffset + 8) == extendedValues[0])
		{
			return RelayItemCube(
				session,
				wirePacket,
				outerRider,
				BitConverter.ToUInt32(wirePacket, rawOffset + 8),
				BitConverter.ToUInt32(wirePacket, rawOffset + 20));
		}

		// Flag pickup uses the same authoritative loopback envelope as an item
		// cube. Action 1 attaches the flag to the addressed kart; the originating
		// client also consumes its own echo to confirm ownership.
		if (rawType == GopTeamFlag && hasExtendedValues &&
			recipientMask == uint.MaxValue && rawLength == 24 &&
			BitConverter.ToUInt32(wirePacket, rawOffset + 4) == GoTeamFlag &&
			BitConverter.ToUInt32(wirePacket, rawOffset + 8) == extendedValues[0] &&
			BitConverter.ToUInt32(wirePacket, rawOffset + 12) == 1)
		{
			return EchoFlagPickup(
				session,
				wirePacket,
				outerRider,
				BitConverter.ToUInt32(wirePacket, rawOffset + 8),
				BitConverter.ToUInt32(wirePacket, rawOffset + 16),
				BitConverter.ToUInt32(wirePacket, rawOffset + 20));
		}

		// A hit on the current carrier is a two-step transfer. Action 3 drops
		// the flag at a world position for one second; action 4 returns it to a
		// fixed position for two seconds. Both transitions are looped back to
		// every racer so the old carrier detaches it before another rider can
		// generate the next action-1 pickup.
		if (rawType == GopTeamFlag && hasExtendedValues &&
			recipientMask == uint.MaxValue && rawLength == 32 &&
			BitConverter.ToUInt32(wirePacket, rawOffset + 4) == GoTeamFlag &&
			BitConverter.ToUInt32(wirePacket, rawOffset + 8) == extendedValues[0])
		{
			uint action = BitConverter.ToUInt32(wirePacket, rawOffset + 12);
			uint transitionTick = action == 3
				? BitConverter.ToUInt32(wirePacket, rawOffset + 28)
				: BitConverter.ToUInt32(wirePacket, rawOffset + 16);
			uint transitionDuration = action == 3 ? 1000u : 2000u;
			if ((action == 3 || action == 4) && wirePacket[12] == 2 &&
				extendedValues[1] == transitionTick &&
				extendedValues[2] == unchecked(transitionTick + transitionDuration))
			{
				return EchoFlagTransition(
					session,
					wirePacket,
					outerRider,
					BitConverter.ToUInt32(wirePacket, rawOffset + 8),
					action,
					transitionTick);
			}
		}

		// Individual flag mode publishes each rider's accumulated possession
		// time through the normal slot relay. This value is also the mode's
		// result metric; the generic 180-second finish time is identical for all
		// riders and cannot determine their order.
		if (rawType == GameFlagGameScore && !hasExtendedValues && rawLength == 8 &&
			recipientMask != 0 && recipientMask != uint.MaxValue)
		{
			return RelayIndividualFlagScore(
				session,
				wirePacket,
				outerRider,
				recipientMask,
				BitConverter.ToUInt32(wirePacket, rawOffset + 4));
		}

		// P236 has already copied this held-item vector into its local kart cache
		// before sending it. Keep a server-side shadow for diagnostics/future
		// validation, but do not echo it and duplicate the local update.
		if (rawType == GameKartItemInfo && !hasExtendedValues)
		{
			return ConsumeItemVector(
				session,
				wirePacket,
				outerRider,
				recipientMask,
				rawOffset,
				rawLength);
		}

		// Item use and every subsequent state transition address the remote
		// racers with a room-slot bit mask (two racers in slots 0/1 therefore
		// produce masks 2/1 respectively). The
		// sender has already applied these operations locally, so echoing it would
		// duplicate placed/flight objects. Relay the exact serialized operation to
		// all other racers; their native handlers create the object, simulate its
		// collision, and emit any follow-up Gop state through this same path.
		if (rawLength >= 8 && recipientMask != 0 && recipientMask != uint.MaxValue &&
			!hasExtendedValues &&
			P236RelayedItemOperations.TryGetValue(rawType, out uint expectedBaseType) &&
			BitConverter.ToUInt32(wirePacket, rawOffset + 4) == expectedBaseType)
		{
			return RelayItemOperation(
				session,
				wirePacket,
				outerRider,
				recipientMask,
				rawType,
				rawOffset,
				rawLength);
		}

		return false;
	}

	private static bool ConsumeItemVector(
		SessionGroup session,
		byte[] wirePacket,
		uint outerRider,
		uint recipientMask,
		int rawOffset,
		int rawLength)
	{
		if (rawLength < 8)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 ITEM] Dropped malformed GameKartItemInfo outerRider={outerRider}: " +
				$"rawLength={rawLength}.");
			return true;
		}

		uint count = BitConverter.ToUInt32(wirePacket, rawOffset + 4);
		if (count > 2 || rawLength != 8 + checked((int)count * 4))
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 ITEM] Dropped malformed GameKartItemInfo outerRider={outerRider}: " +
				$"count={count}, rawLength={rawLength}.");
			return true;
		}

		if (!TryGetItemRelayTargets(
			session,
			outerRider,
			recipientMask,
			includeSender: false,
			out LegacyRoom room,
			out LegacyRoomMember member,
			out _,
			out string rejection))
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 ITEM] Ignored GameKartItemInfo outerRider={outerRider}, " +
				$"mask=0x{recipientMask:X8}: {rejection}.");
			return true;
		}

		uint senderBit = 1u << member.SlotId;
		if (recipientMask != senderBit)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 ITEM] Dropped GameKartItemInfo room={room.Id}, slot={member.SlotId}: " +
				$"mask=0x{recipientMask:X8}, expected=0x{senderBit:X8}.");
			return true;
		}

		uint[] items = new uint[count];
		for (int index = 0; index < items.Length; index++)
		{
			items[index] = BitConverter.ToUInt32(wirePacket, rawOffset + 8 + index * 4);
		}

		lock (room.SyncRoot)
		{
			if (ReferenceEquals(FindMember(room, session), member))
			{
				member.ItemVector = items;
			}
		}
		return true;
	}

	private static bool RelayItemCube(
		SessionGroup session,
		byte[] wirePacket,
		uint outerRider,
		uint cubeObject,
		uint collisionTick)
	{
		if (!TryGetItemRelayTargets(
			session,
			outerRider,
			uint.MaxValue,
			includeSender: true,
			out LegacyRoom room,
			out LegacyRoomMember member,
			out List<SessionGroup> targets,
			out string rejection))
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 ITEM] Ignored GopCube outerRider={outerRider}, " +
				$"cube=0x{cubeObject:X8}, tick={collisionTick}: {rejection}.");
			return true;
		}

		SendExactGameSlot(wirePacket, targets);
		LegacyPacketTrace.LogEvent(
			$"[2005 ITEM] Echoed GopCube room={room.Id}, slot={member.SlotId}, " +
			$"outerRider={outerRider}, cube=0x{cubeObject:X8}, tick={collisionTick}, " +
			$"targets={targets.Count} (sender included).");
		return true;
	}

	private static bool RelayItemOperation(
		SessionGroup session,
		byte[] wirePacket,
		uint outerRider,
		uint recipientMask,
		uint rawType,
		int rawOffset,
		int rawLength)
	{
		uint objectId = rawLength >= 12
			? BitConverter.ToUInt32(wirePacket, rawOffset + 8)
			: 0;
		uint operationState = rawLength >= 16
			? BitConverter.ToUInt32(wirePacket, rawOffset + 12)
			: 0;
		string operationName = $"0x{rawType:X8}";

		if (!TryGetItemRelayTargets(
			session,
			outerRider,
			recipientMask,
			includeSender: false,
			out LegacyRoom room,
			out LegacyRoomMember member,
			out List<SessionGroup> targets,
			out string rejection))
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 ITEM] Ignored {operationName} outerRider={outerRider}, " +
				$"object=0x{objectId:X8}, state={operationState}: {rejection}.");
			return true;
		}

		SendExactGameSlot(wirePacket, targets);
		LegacyPacketTrace.LogEvent(
			$"[2005 ITEM] Relayed {operationName} room={room.Id}, slot={member.SlotId}, " +
			$"outerRider={outerRider}, mask=0x{recipientMask:X8}, " +
			$"object=0x{objectId:X8}, state={operationState}, " +
			$"rawLength={rawLength}, targets={targets.Count} (sender excluded).");
		return true;
	}

	private static bool EchoFlagPickup(
		SessionGroup session,
		byte[] wirePacket,
		uint outerRider,
		uint flagObject,
		uint kartObject,
		uint collisionTick)
	{
		if (!TryGetItemRelayTargets(
			session,
			outerRider,
			uint.MaxValue,
			includeSender: true,
			out LegacyRoom room,
			out LegacyRoomMember member,
			out List<SessionGroup> targets,
			out string rejection))
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored pickup outerRider={outerRider}, " +
				$"flag=0x{flagObject:X8}, tick={collisionTick}: {rejection}.");
			return true;
		}

		if (room.GameType != 7 && room.GameType != 8)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored pickup room={room.Id}, slot={member.SlotId}: " +
				$"game type {room.GameType} is not a flag mode.");
			return true;
		}

		int previousOwnerSlot = -1;
		bool overtimeRestartPending = false;
		uint overtimeStartTick = 0;
		lock (room.SyncRoot)
		{
			overtimeStartTick = room.FlagTeamOvertimeStartTick;
			overtimeRestartPending = room.GameType == 7 && room.FlagTeamOvertime &&
				IsTickBefore(unchecked((uint)Environment.TickCount64), overtimeStartTick);
			if (!overtimeRestartPending &&
				!room.FlagOwnerSlots.TryGetValue(flagObject, out previousOwnerSlot))
			{
				previousOwnerSlot = -1;
			}
			if (!overtimeRestartPending)
			{
				room.FlagOwnerSlots[flagObject] = member.SlotId;
			}
		}

		if (overtimeRestartPending)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored pickup during overtime restart room={room.Id}, " +
				$"slot={member.SlotId}, flag=0x{flagObject:X8}, " +
				$"startTick={overtimeStartTick}.");
			return true;
		}

		SendExactGameSlot(wirePacket, targets);
		LegacyPacketTrace.LogEvent(
			$"[2005 FLAG] Echoed pickup room={room.Id}, slot={member.SlotId}, " +
			$"outerRider={outerRider}, flag=0x{flagObject:X8}, " +
			$"kart=0x{kartObject:X8}, tick={collisionTick}, " +
			$"previousOwnerSlot={previousOwnerSlot}, targets={targets.Count} (sender included).");
		return true;
	}

	private static bool RelayIndividualFlagScore(
		SessionGroup session,
		byte[] wirePacket,
		uint outerRider,
		uint recipientMask,
		uint score)
	{
		if (!TryGetItemRelayTargets(
			session,
			outerRider,
			recipientMask,
			includeSender: false,
			out LegacyRoom room,
			out LegacyRoomMember member,
			out List<SessionGroup> targets,
			out string rejection))
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored individual score outerRider={outerRider}, " +
				$"mask=0x{recipientMask:X8}, score={score}: {rejection}.");
			return true;
		}

		if (room.GameType != 8)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored individual score room={room.Id}, slot={member.SlotId}: " +
				$"game type {room.GameType} is not individual flag mode.");
			return true;
		}

		uint previousScore;
		lock (room.SyncRoot)
		{
			previousScore = member.FlagScore;
			member.FlagScore = score;
			LegacyRaceParticipantSnapshot participant =
				FindRaceParticipantLocked(room, member.SlotId);
			if (participant != null)
			{
				participant.FlagScore = score;
			}
		}

		SendExactGameSlot(wirePacket, targets);
		if (previousScore == 0 || score == 0 || previousScore / 10000u != score / 10000u)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Relayed individual score room={room.Id}, slot={member.SlotId}, " +
				$"outerRider={outerRider}, mask=0x{recipientMask:X8}, " +
				$"score={previousScore}->{score}, targets={targets.Count} (sender excluded).");
		}
		return true;
	}

	private static bool EchoFlagTransition(
		SessionGroup session,
		byte[] wirePacket,
		uint outerRider,
		uint flagObject,
		uint action,
		uint transitionTick)
	{
		if (!TryGetItemRelayTargets(
			session,
			outerRider,
			uint.MaxValue,
			includeSender: true,
			out LegacyRoom room,
			out LegacyRoomMember member,
			out List<SessionGroup> targets,
			out string rejection))
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored action {action} outerRider={outerRider}, " +
				$"flag=0x{flagObject:X8}, tick={transitionTick}: {rejection}.");
			return true;
		}

		if (room.GameType != 7 && room.GameType != 8)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored action {action} room={room.Id}, slot={member.SlotId}: " +
				$"game type {room.GameType} is not a flag mode.");
			return true;
		}

		int previousOwnerSlot = -1;
		bool overtimeRestartPending = false;
		uint overtimeStartTick = 0;
		lock (room.SyncRoot)
		{
			overtimeStartTick = room.FlagTeamOvertimeStartTick;
			overtimeRestartPending = room.GameType == 7 && room.FlagTeamOvertime &&
				IsTickBefore(unchecked((uint)Environment.TickCount64), overtimeStartTick);
			if (!overtimeRestartPending &&
				room.FlagOwnerSlots.TryGetValue(flagObject, out int ownerSlot))
			{
				previousOwnerSlot = ownerSlot;
				room.FlagOwnerSlots.Remove(flagObject);
			}
		}

		if (overtimeRestartPending)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored action {action} during overtime restart " +
				$"room={room.Id}, slot={member.SlotId}, flag=0x{flagObject:X8}, " +
				$"startTick={overtimeStartTick}.");
			return true;
		}

		SendExactGameSlot(wirePacket, targets);
		string transitionName = action == 3 ? "drop" : "return";
		LegacyPacketTrace.LogEvent(
			$"[2005 FLAG] Echoed {transitionName} room={room.Id}, slot={member.SlotId}, " +
			$"outerRider={outerRider}, flag=0x{flagObject:X8}, tick={transitionTick}, " +
			$"previousOwnerSlot={previousOwnerSlot}, targets={targets.Count} (sender included).");
		return true;
	}

	private static bool TryGetItemRelayTargets(
		SessionGroup session,
		uint outerRider,
		uint recipientMask,
		bool includeSender,
		out LegacyRoom room,
		out LegacyRoomMember member,
		out List<SessionGroup> targets,
		out string rejection)
	{
		room = LegacyRoomManager.GetFor(session);
		member = null;
		targets = null;
		rejection = null;
		if (room == null)
		{
			rejection = "not in a room";
			return false;
		}

		lock (room.SyncRoot)
		{
			member = FindMember(room, session);
			if (!IsItemEnabledGameType(room.GameType))
			{
				rejection = $"room game type {room.GameType} does not enable kart items";
				return false;
			}
			if (!room.Started || !room.StartSignalSent || member == null ||
				!member.Initialized || !member.LoadedInGame ||
				member.FinishedInGame ||
				Array.IndexOf(room.RaceGridSlots, member.SlotId) < 0)
			{
				rejection = "sender is not an active loaded racer";
				return false;
			}
			if (outerRider != (uint)member.SlotId)
			{
				rejection = $"outer rider {outerRider} does not match sender slot {member.SlotId}";
				return false;
			}

			targets = new List<SessionGroup>();
			foreach (int slot in room.RaceGridSlots)
			{
				if (slot < 0 || slot >= room.Members.Length)
				{
					continue;
				}

				LegacyRoomMember targetMember = room.Members[slot];
				if (targetMember == null || !targetMember.Initialized ||
					!targetMember.LoadedInGame || targetMember.FinishedInGame ||
					(!includeSender && ReferenceEquals(targetMember.Session, session)))
				{
					continue;
				}

				uint targetBit = 1u << slot;
				if (includeSender || (recipientMask & targetBit) != 0)
				{
					targets.Add(targetMember.Session);
				}
			}

			foreach (LegacyRoomMember observer in room.Members)
			{
				if (observer != null && observer.IsObserver && observer.Initialized &&
					observer.LoadedInGame)
				{
					targets.Add(observer.Session);
				}
			}
		}
		return true;
	}

	private static void SendExactGameSlot(
		byte[] wirePacket,
		IReadOnlyList<SessionGroup> targets)
	{
		using OutPacket relay = new OutPacket();
		relay.WriteBytes(wirePacket);
		foreach (SessionGroup target in targets)
		{
			target.Client.Send(relay);
		}
	}

	private static bool TryParseGameSlotEnvelope(
		byte[] wirePacket,
		out uint outerRider,
		out uint slotKind,
		out bool hasExtendedValues,
		out uint[] extendedValues,
		out int rawOffset,
		out int rawLength,
		out uint rawType)
	{
		const int hashLength = 4;
		const int maximumRawLength = 960;
		outerRider = 0;
		slotKind = 0;
		hasExtendedValues = false;
		extendedValues = Array.Empty<uint>();
		rawOffset = 0;
		rawLength = 0;
		rawType = 0;
		if (wirePacket == null || wirePacket.Length < hashLength + 13 ||
			BitConverter.ToUInt32(wirePacket, 0) != GameSlot)
		{
			return false;
		}

		outerRider = BitConverter.ToUInt32(wirePacket, hashLength);
		slotKind = BitConverter.ToUInt32(wirePacket, hashLength + 4);
		hasExtendedValues = wirePacket[hashLength + 8] != 0;
		int position = hashLength + 9;
		if (hasExtendedValues)
		{
			if (wirePacket.Length < position + 12 + 4)
			{
				return false;
			}
			extendedValues = new uint[3];
			for (int index = 0; index < extendedValues.Length; index++)
			{
				extendedValues[index] = BitConverter.ToUInt32(wirePacket, position);
				position += 4;
			}
		}

		rawLength = BitConverter.ToInt32(wirePacket, position);
		rawOffset = position + 4;
		if (rawLength < 4 || rawLength > maximumRawLength ||
			wirePacket.Length != rawOffset + rawLength)
		{
			return false;
		}

		rawType = BitConverter.ToUInt32(wirePacket, rawOffset);
		return true;
	}

	private static void HandleChannelSwitch(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 3, "PqChannelSwitch");
		byte requestedChannel = packet.ReadByte();
		ushort token = packet.ReadUShort();
		RequireConsumed(packet, "PqChannelSwitch");

		if (!LegacyChannelPolicy.TryGetSpeedType(requestedChannel, out byte speedType))
		{
			throw new InvalidDataException($"Unknown 2005 channel id {requestedChannel}.");
		}

		ushort selectedChannelId = requestedChannel;
		session.Multiplayer.Channel = requestedChannel;
		session.Multiplayer.ChannelToken = token;

		IPEndPoint endpoint = RouterListener.CurrentUDPServer;
		if (endpoint == null)
		{
			throw new InvalidOperationException("The 2005 UDP endpoint is not available.");
		}

		using OutPacket reply = new OutPacket("PrChannelSwitch");
		reply.WriteInt(0);
		reply.WriteUShort(selectedChannelId);
		reply.WriteUShort(token);
		reply.WriteEndPoint(endpoint);
		session.Client.Send(reply);
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Channel switch requested={requestedChannel}, selected={selectedChannelId}, " +
			$"speedType={speedType}, token={token}, endpoint={endpoint}.");
	}

	private static void HandleChannelMoveIn(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 29, "PqChannelMovein");
		uint claimedUserNo = packet.ReadUInt();
		ushort channelId = packet.ReadUShort();
		ushort token = packet.ReadUShort();
		byte[] proof = packet.ReadBytes(21);
		RequireConsumed(packet, "PqChannelMovein");
		uint provisionalUserNo = session.Profile.UserNo;
		bool identityBound = RouterListener.TryBindChannelProfile(session, claimedUserNo);
		byte speedType = 0;
		bool channelValid = channelId <= byte.MaxValue &&
			LegacyChannelPolicy.TryGetSpeedType((byte)channelId, out speedType);
		bool accepted = identityBound && channelValid;
		if (accepted)
		{
			session.Multiplayer.Channel = (byte)channelId;
			session.Multiplayer.ChannelToken = token;
		}

		using OutPacket reply = new OutPacket("PrChannelMoveIn");
		reply.WriteByte(accepted ? (byte)1 : (byte)0);
		session.Client.Send(reply);
		if (!accepted)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 MP] Rejected channel move-in claimedUserNo={claimedUserNo}, " +
				$"provisionalUserNo={provisionalUserNo}, identityBound={identityBound}, " +
				$"channelId={channelId}, channelValid={channelValid}, token={token}, " +
				$"proof={BitConverter.ToString(proof)}.");
			return;
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Channel move-in claimedUserNo={claimedUserNo}, " +
			$"provisionalUserNo={provisionalUserNo}, profileUserNo={session.Profile.UserNo}, " +
			$"channelId={channelId}, speedType={speedType}, token={token}, " +
			$"proof={BitConverter.ToString(proof)}.");
	}

	private static void HandleCurrentGp(SessionGroup session)
	{
		using OutPacket reply = new OutPacket("ChGetCurrentGpReplyPacket");
		reply.WriteUInt(0);
		reply.WriteUInt(0);
		reply.WriteByte(0);
		session.Client.Send(reply);
	}

	private static void HandleClientUdpAddress(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 6, "ChClientUdpAddrPacket");
		IPEndPoint reportedEndPoint = packet.ReadEndPoint();
		RequireConsumed(packet, "ChClientUdpAddrPacket");
		session.Multiplayer.ReportedUdpEndPoint = reportedEndPoint;
		LegacyPacketTrace.LogEvent($"[2005 MP] Client reported UDP endpoint={reportedEndPoint}.");
	}

	private static void HandleRoomList(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 8, "ChGetRoomListRequestPacket");
		int page = packet.ReadInt();
		int unknown = packet.ReadInt();
		RequireConsumed(packet, "ChGetRoomListRequestPacket");

		// The 2005 wire shape is page/total/page-count/vector-count followed by
		// compact entries. Each entry ends after exactly five state bytes.
		const int pageSize = 8;
		IReadOnlyList<LegacyRoomListEntry> rooms =
			LegacyRoomManager.GetList(session.Multiplayer.Channel);
		int normalizedPage = Math.Max(0, page);
		int totalPages = Math.Max(1, (rooms.Count + pageSize - 1) / pageSize);
		int firstRoom = normalizedPage <= int.MaxValue / pageSize
			? normalizedPage * pageSize
			: rooms.Count;
		int roomsOnPage = firstRoom < rooms.Count
			? Math.Min(pageSize, rooms.Count - firstRoom)
			: 0;
		using OutPacket reply = new OutPacket("ChGetRoomListReplyPacket");
		reply.WriteInt(page);                 // current page
		reply.WriteInt(0);                     // reserved/unused in the 2005 client
		reply.WriteInt(totalPages);            // total page count
		reply.WriteInt(roomsOnPage);
		for (int index = firstRoom; index < firstRoom + roomsOnPage; index++)
		{
			LegacyRoomListEntry room = rooms[index];
			reply.WriteUShort(unchecked((ushort)room.Id));
			reply.WriteString(room.Name);
			reply.WriteUInt(room.Track);
			reply.WriteByte(room.Locked ? (byte)1 : (byte)0);
			reply.WriteByte(room.GameType);
			reply.WriteByte(room.Started ? (byte)1 : (byte)0);
			reply.WriteByte(8);
			reply.WriteByte(unchecked((byte)Math.Min(byte.MaxValue, room.Occupants)));
		}
		session.Client.Send(reply);
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Room list channel={session.Multiplayer.Channel}, page={page}, " +
			$"unknown={unknown}, channelRooms={rooms.Count}, " +
			$"roomsOnPage={roomsOnPage}.");
	}

	private static void HandleCreateRoom(SessionGroup session, InPacket packet)
	{
		string roomName = packet.ReadString();
		string password = packet.ReadString();
		// This field is serialized as a plain byte by the 2005 client. Treating it
		// as a spec-key encoded byte turns the normal value 1 into 200, which is
		// then echoed in GrSessionData and sends the client through an invalid
		// game-mode branch while entering GameReadyStage.
		byte gameType = packet.ReadByte();
		int unknown1 = packet.ReadInt();
		int unknown2 = packet.ReadInt();
		byte[] trackCandidates = packet.ReadBytes(32);
		byte[] proof = packet.ReadBytes(21);
		RequireConsumed(packet, "ChCreateRoomRequestPacket");
		byte channel = session.Multiplayer.Channel;
		if (!LegacyChannelPolicy.TryGetSpeedType(channel, out byte speedType))
		{
			using OutPacket rejectedReply = new OutPacket("ChCreateRoomReplyPacket");
			rejectedReply.WriteByte(0);
			session.Client.Send(rejectedReply);
			LegacyPacketTrace.LogEvent(
				$"[2005 MP] Rejected room creation without a valid channel; " +
				$"userNo={session.Profile.UserNo}, channel={channel}.");
			return;
		}

		LegacyRoom room = LegacyRoomManager.Create(
			session,
			roomName,
			password,
			gameType,
			channel,
			speedType,
			unknown1,
			unknown2,
			trackCandidates,
			proof);

		using OutPacket reply = new OutPacket("ChCreateRoomReplyPacket");
		reply.WriteByte(1);
		session.Client.Send(reply);
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Created room id={room.Id}, name=\"{room.Name}\", gameType={room.GameType}, " +
			$"channel={room.Channel}, speedType={room.SpeedType}, " +
			$"ownerSlot={room.OwnerSlot}, " +
			$"ownerObserver={LegacyObserverPolicy.IsObserver(session.Profile)}, " +
			$"ownerTeam={(LegacyObserverPolicy.IsObserver(session.Profile) ? 0 : IsTeamGameType(room.GameType) ? 2 : 0)}, " +
			$"locked={!string.IsNullOrEmpty(room.Password)}.");
	}

	private static void HandleJoinRoom(SessionGroup session, InPacket packet)
	{
		RequireAvailableAtLeast(packet, 28, "ChJoinRoomRequestPacket");
		ushort roomId = packet.ReadUShort();
		byte unknown = packet.ReadByte();
		string password = packet.ReadString();
		RequireAvailable(packet, 21, "ChJoinRoomRequestPacket proof");
		byte[] proof = packet.ReadBytes(21);
		RequireConsumed(packet, "ChJoinRoomRequestPacket");

		byte status = 1;
		LegacyRoom room = null;
		LegacyRoomMember joinedMember = null;
		if (LegacyRoomManager.TryJoin(session, roomId, password, out room, out joinedMember))
		{
			status = 0;
		}

		// The 2005 reply is exactly one spec-key encoded status byte. A logical
		// status of zero causes the client to enter GameReadyStage and send the
		// bodyless GrFirst request after its initialization phase.
		using OutPacket reply = new OutPacket("ChJoinRoomReplyPacket");
		reply.WriteEncByte(status);
		session.Client.Send(reply);
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Join room id={roomId}, channel={session.Multiplayer.Channel}, " +
			$"unknown={unknown}, status={status}, " +
			$"slot={joinedMember?.SlotId ?? -1}, team={joinedMember?.Team ?? 0}, " +
			$"observer={joinedMember?.IsObserver ?? false}, " +
			$"ownerSlot={room?.OwnerSlot ?? -1}, " +
			$"proof={BitConverter.ToString(proof)}.");
	}

	private static void HandleSecedeRoom(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 0, "ChSecedeRoomRequestPacket");
		RequireConsumed(packet, "ChSecedeRoomRequestPacket");

		int previousRoomId = session.Multiplayer.RoomId;
		// Detach before replying so any immediate lobby request observes a clean
		// session. Treat repeated leave requests as idempotent success to ensure
		// the client's pending UI flag is always released.
		LegacyRoom previousRoom = LegacyRoomManager.Remove(session);

		using OutPacket reply = new OutPacket("ChSecedeRoomReplyPacket");
		reply.WriteEncByte(1);
		session.Client.Send(reply);
		if (previousRoom != null)
		{
			BroadcastSlotData(previousRoom);
		}
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Left room previousRoomId={previousRoomId}; success=True.");
	}

	private static void HandleEventTempReward(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 4, "LoRqEventTempRewardPacket");
		uint checkedMask = packet.ReadUInt();
		RequireConsumed(packet, "LoRqEventTempRewardPacket");

		using OutPacket reply = new OutPacket("LoRpEventTempRewardPacket");
		reply.WriteInt(0);            // result
		reply.WriteUInt(checkedMask); // acknowledge the client's one-shot request
		reply.WriteByte(0);           // no reward
		reply.WriteInt(0);            // reward id
		session.Client.Send(reply);
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Event temp reward acknowledged mask=0x{checkedMask:X8}; no reward.");
	}

	private static void HandleFirstRoomRequest(SessionGroup session)
	{
		LegacyRoom room = LegacyRoomManager.GetFor(session);
		if (room == null)
		{
			LegacyPacketTrace.LogEvent("[2005 MP] GrFirstRequestPacket arrived without a room.");
			return;
		}

		LegacyRoomMember member;
		lock (room.SyncRoot)
		{
			member = FindMember(room, session);
			if (member == null)
			{
				LegacyPacketTrace.LogEvent("[2005 MP] GrFirstRequestPacket member vanished before initialization.");
				return;
			}

			member.Initialized = true;
		}

		using OutPacket reply = new OutPacket("GrSessionDataPacket");
		WriteSessionDataBody(reply, room);
		session.Client.Send(reply);

		BroadcastSlotData(room);
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Initialized room id={room.Id} slot={member.SlotId}; " +
			"broadcast initial session slots.");
	}

	private static void HandleSetSlotState(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 4, "GrRequestSetSlotStatePacket");
		int requestedStatus = packet.ReadInt();
		RequireConsumed(packet, "GrRequestSetSlotStatePacket");

		LegacyRoom room = LegacyRoomManager.GetFor(session);
		// These are slot presentation states, not room authority: the ready UI
		// sends 3 to ready and 2 to cancel, while 5 is also used while entering
		// or temporarily leaving the ready card for customization.
		bool accepted = false;
		int slotId = -1;
		if (room != null && (requestedStatus == 2 || requestedStatus == 3 || requestedStatus == 5))
		{
			lock (room.SyncRoot)
			{
				LegacyRoomMember member = FindMember(room, session);
				if (!room.Started && member != null && !member.IsObserver && member.Initialized &&
					(!IsTeamGameType(room.GameType) || member.Team == 1 || member.Team == 2))
				{
					member.Status = requestedStatus;
					slotId = member.SlotId;
					accepted = true;
				}
			}
		}

		// The 2005 reply has only one spec-key encoded boolean. The newer
		// user/slot/status fields do not exist in this client build.
		using OutPacket reply = new OutPacket("GrReplySetSlotStatePacket");
		reply.WriteEncByte(accepted ? (byte)1 : (byte)0);
		session.Client.Send(reply);

		if (accepted)
		{
			BroadcastSlotData(room);
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Slot state requested={requestedStatus}, accepted={accepted}, " +
			$"roomId={room?.Id ?? -1}, slot={slotId}.");
	}

	private static void HandleChangeTeam(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 1, "GrChangeTeamPacket");
		// P236 writes the logical 1/2 team ID as one raw byte through its
		// RawBufferStream serializer.
		byte requestedTeam = packet.ReadByte();
		RequireConsumed(packet, "GrChangeTeamPacket");

		LegacyRoom room = LegacyRoomManager.GetFor(session);
		bool accepted = false;
		bool changed = false;
		int slotId = -1;
		byte previousTeam = 0;
		byte currentTeam = 0;
		int redCount = 0;
		int blueCount = 0;
		if (room != null)
		{
			lock (room.SyncRoot)
			{
				foreach (LegacyRoomMember roomMember in room.Members)
				{
					if (roomMember?.Team == 1)
					{
						redCount++;
					}
					else if (roomMember?.Team == 2)
					{
						blueCount++;
					}
				}

				LegacyRoomMember member = FindMember(room, session);
				if (member != null)
				{
					slotId = member.SlotId;
					previousTeam = member.Team;
					currentTeam = member.Team;
					if (!room.Started && !member.IsObserver && member.Initialized &&
						IsTeamGameType(room.GameType) &&
						(requestedTeam == 1 || requestedTeam == 2))
					{
						if (member.Team == requestedTeam)
						{
							accepted = true;
						}
						else
						{
							int targetTeamCount = requestedTeam == 1 ? redCount : blueCount;
							if (targetTeamCount < 4)
							{
								accepted = true;
								member.Team = requestedTeam;
								member.Status = 2;
								currentTeam = requestedTeam;
								changed = true;
								if (previousTeam == 1)
								{
									redCount--;
								}
								else if (previousTeam == 2)
								{
									blueCount--;
								}

								if (requestedTeam == 1)
								{
									redCount++;
								}
								else
								{
									blueCount++;
								}
							}
						}
					}
				}
			}
		}

		// P236 has no GrChangeTeam reply packet. A full slot snapshot both
		// acknowledges an accepted change and releases the client's pending UI.
		if (changed)
		{
			BroadcastSlotData(room);
		}
		else if (room != null)
		{
			SendSlotData(session, room);
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Team change requested={requestedTeam}, accepted={accepted}, changed={changed}, " +
			$"roomId={room?.Id ?? -1}, slot={slotId}, team={previousTeam}->{currentTeam}, " +
			$"teams=R{redCount}/B{blueCount}.");
	}

	private static void HandleChangeTrack(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 36, "GrChangeTrackPacket");
		uint track = packet.ReadUInt();
		byte[] trackCandidates = packet.ReadBytes(32);
		RequireConsumed(packet, "GrChangeTrackPacket");

		LegacyRoom room = LegacyRoomManager.GetFor(session);
		bool isLocalOwner = false;
		if (room != null)
		{
			lock (room.SyncRoot)
			{
				isLocalOwner = IsOwner(room, session);
				if (isLocalOwner)
				{
					room.Track = track;
					room.TrackCandidates = trackCandidates;
				}
			}
		}

		if (!isLocalOwner)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 MP] Rejected track change track={track}; roomId={room?.Id ?? -1}, " +
				$"localOwner={isLocalOwner}.");
			if (room != null)
			{
				SendSlotData(session, room);
			}
			return;
		}

		BroadcastSlotData(room);
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Changed room id={room.Id} track={track}; " +
			$"trackCandidates={BitConverter.ToString(trackCandidates)}.");
	}

	private static void HandleStartRoom(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 0, "GrRequestStartPacket");
		RequireConsumed(packet, "GrRequestStartPacket");

		LegacyRoom room = LegacyRoomManager.GetFor(session);
		bool isOwner = false;
		bool guestsReady = true;
		bool alreadyStarted = false;
		bool canStart = false;
		int initializedPlayerCount = 0;
		int initializedRedCount = 0;
		int initializedBlueCount = 0;
		int invalidTeamCount = 0;
		bool teamsReady = true;
		bool teamGame = false;
		int observerCount = 0;
		uint selectedTrack = 0;
		string raceGrid = string.Empty;
		List<SessionGroup> commandTargets = new List<SessionGroup>();
		OutPacket command = null;
		if (room != null)
		{
			lock (room.SyncRoot)
			{
				isOwner = IsOwner(room, session);
				alreadyStarted = room.Started;
				teamGame = IsTeamGameType(room.GameType);
				selectedTrack = room.Track;
				foreach (LegacyRoomMember member in room.Members)
				{
					if (member == null)
					{
						continue;
					}
					if (member.IsObserver)
					{
						observerCount++;
						continue;
					}

					if (member.Initialized)
					{
						initializedPlayerCount++;
						if (teamGame)
						{
							if (member.Team == 1)
							{
								initializedRedCount++;
							}
							else if (member.Team == 2)
							{
								initializedBlueCount++;
							}
							else
							{
								invalidTeamCount++;
							}
						}
					}

					if (member.SlotId != room.OwnerSlot &&
						(!member.Initialized || member.Status != 3))
					{
						guestsReady = false;
					}
				}

				// Track selector zero is the valid 2005 allRandom mode. Do not treat
				// it (or its all-zero candidate mask) as an uninitialized room.
				teamsReady = !teamGame ||
					(initializedRedCount > 0 && initializedRedCount == initializedBlueCount &&
						invalidTeamCount == 0);
				canStart = isOwner && !alreadyStarted && initializedPlayerCount >= 2 &&
					guestsReady && teamsReady;
				if (canStart)
				{
					List<int> raceGridSlots = new List<int>();
					List<LegacyRoomMember> raceGridMembers = new List<LegacyRoomMember>();
					for (int slot = 0; slot < room.Members.Length; slot++)
					{
						LegacyRoomMember member = room.Members[slot];
						if (member == null)
						{
							continue;
						}

						member.LoadedInGame = false;
						if (member.IsObserver || !member.Initialized)
						{
							continue;
						}

						member.FinishedInGame = false;
						member.FinishTime = uint.MaxValue;
						member.FlagScore = 0;
						raceGridSlots.Add(slot);
						raceGridMembers.Add(member);
					}

					// The byte following team in each 2005 rider record is the
					// previous-rank/start-grid value consumed by the race loader. Keep
					// the physical room slot stable, sort by the already-held grid value,
					// and compact it to unique zero-based positions for this race.
					raceGridMembers.Sort((left, right) =>
					{
						int byGrid = left.GridPosition.CompareTo(right.GridPosition);
						if (byGrid != 0)
						{
							return byGrid;
						}

						int byJoin = left.JoinSequence.CompareTo(right.JoinSequence);
						return byJoin != 0 ? byJoin : left.SlotId.CompareTo(right.SlotId);
					});
					raceGridSlots.Clear();
					for (int position = 0; position < raceGridMembers.Count; position++)
					{
						raceGridMembers[position].GridPosition = checked((byte)position);
						raceGridSlots.Add(raceGridMembers[position].SlotId);
					}

					room.RaceGridSlots = raceGridSlots.ToArray();
					LegacyRaceParticipantSnapshot[] raceParticipants =
						new LegacyRaceParticipantSnapshot[raceGridMembers.Count];
					for (int position = 0; position < raceGridMembers.Count; position++)
					{
						LegacyRoomMember racer = raceGridMembers[position];
						raceParticipants[position] = new LegacyRaceParticipantSnapshot
						{
							SlotId = racer.SlotId,
							Team = teamGame ? racer.Team : (byte)0,
							GridPosition = racer.GridPosition
						};
					}
					room.RaceParticipants = raceParticipants;
					room.RedTeamBoosterGauge = 0f;
					room.BlueTeamBoosterGauge = 0f;
					room.FlagOwnerSlots.Clear();
					room.RedFlagScore = 0;
					room.BlueFlagScore = 0;
					room.FlagTeamOvertime = false;
					room.FlagTeamOvertimeStartTick = 0;
					room.StartSignalSent = false;
					room.StartTick = 0;
					room.RaceEnding = false;
					room.RaceEndTick = 0;
					room.RaceGeneration++;
					raceGrid = DescribeRaceGridLocked(room);

					command = new OutPacket("GrCommandStartPacket");
					// Nested packets are encoded as their raw hash followed immediately by
					// the packet body. The 2005 command ends after the two track seeds.
					command.WriteUInt(Hash("GrSessionDataPacket"));
					WriteSessionDataBody(command, room);
					command.WriteUInt(Hash("GrSlotDataPacket"));
					// In the lobby the room master is state 2, but the 2005 race loader
					// only instantiates remote racers whose start snapshot state is 3.
					// Encode every admitted participant as racing without mutating the
					// lobby state that will be needed when the race returns to the room.
					WriteSlotDataBody(command, room, forRaceStart: true);
					command.WriteInt(0);
					command.WriteInt(0);
					room.Started = true;
					commandTargets = GetMemberSessions(room, initializedOnly: true);
				}
			}
		}

		// Client status values map 1=warnAllReady, 2=warnAlone, and
		// 3=warnUnmatchTeam. Keep the solo warning ahead of the team check.
		int replyStatus = canStart
			? 0
			: initializedPlayerCount < 2
				? 2
				: teamGame && !teamsReady
					? 3
					: 1;
		using (OutPacket reply = new OutPacket("GrReplyStartPacket"))
		{
			reply.WriteInt(replyStatus);
			session.Client.Send(reply);
		}

		if (!canStart)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 MP] Rejected room start roomId={room?.Id ?? -1}, owner={isOwner}, " +
				$"started={alreadyStarted}, players={initializedPlayerCount}, guestsReady={guestsReady}, " +
				$"teamsReady={teamsReady}, observers={observerCount}, " +
				$"teams=R{initializedRedCount}/B{initializedBlueCount}/" +
				$"invalid{invalidTeamCount}, track={selectedTrack}, reply={replyStatus}.");
			return;
		}

		using (command)
		{
			foreach (SessionGroup target in commandTargets)
			{
				target.Client.Send(command);
			}
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Started room id={room.Id}, track={selectedTrack}, seeds=(0,0), " +
			$"teams=R{initializedRedCount}/B{initializedBlueCount}, " +
			$"observers={observerCount}, commandTargets={commandTargets.Count}, grid=[{raceGrid}].");
	}

	private static void HandleTeamBoosterGauge(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 5, "GameTeamBoosterRequestAddGaugePacket");
		byte requestedTeam = packet.ReadByte();
		float requestedValue = packet.ReadFloat();
		RequireConsumed(packet, "GameTeamBoosterRequestAddGaugePacket");

		LegacyRoom room = LegacyRoomManager.GetFor(session);
		List<SessionGroup> targets = null;
		bool accepted = false;
		bool completed = false;
		int slotId = -1;
		int teamPlayerCount = 0;
		float previousGauge = 0f;
		float contribution = 0f;
		float broadcastGauge = 0f;
		string rejection = string.Empty;
		if (room == null)
		{
			rejection = "no active room";
		}
		else
		{
			lock (room.SyncRoot)
			{
				LegacyRoomMember member = FindMember(room, session);
				LegacyRaceParticipantSnapshot participant = member == null
					? null
					: FindRaceParticipantLocked(room, member.SlotId);
				slotId = member?.SlotId ?? -1;
				if (!room.Started)
				{
					rejection = "race is not active";
				}
				else if (!room.StartSignalSent)
				{
					rejection = "race start is not synchronized";
				}
				else if (room.RaceEnding)
				{
					rejection = "race is ending";
				}
				else if (!IsSpeedTeamGameType(room.GameType))
				{
					rejection = $"game type {room.GameType} is not speed team";
				}
				else if (member == null || !member.Initialized || participant == null)
				{
					rejection = "sender is not an active racer";
				}
				else if (participant.Finished)
				{
					rejection = "sender has already finished";
				}
				else if ((requestedTeam != 1 && requestedTeam != 2) ||
					member.Team != requestedTeam || participant.Team != requestedTeam)
				{
					rejection = $"team mismatch member={member.Team}, race={participant.Team}";
				}
				else if (!float.IsFinite(requestedValue) || requestedValue <= 0f)
				{
					rejection = "gauge contribution is not finite and positive";
				}
				else
				{
					teamPlayerCount = CountActiveRaceTeamMembersLocked(room, requestedTeam);
					if (teamPlayerCount <= 0)
					{
						rejection = "team has no active racers";
					}
					else
					{
						previousGauge = requestedTeam == 1
							? room.RedTeamBoosterGauge
							: room.BlueTeamBoosterGauge;
						contribution = requestedValue * 0.000125f / teamPlayerCount;
						broadcastGauge = Math.Min(1f, previousGauge + contribution);
						completed = broadcastGauge >= 1f;
						if (requestedTeam == 1)
						{
							room.RedTeamBoosterGauge = completed ? 0f : broadcastGauge;
						}
						else
						{
							room.BlueTeamBoosterGauge = completed ? 0f : broadcastGauge;
						}

						targets = GetActiveRaceTeamSessionsLocked(room, requestedTeam);
						accepted = true;
					}
				}
			}
		}

		if (!accepted)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 TEAM BOOSTER] Rejected room={room?.Id ?? -1}, slot={slotId}, " +
				$"team={requestedTeam}, value={requestedValue:R}: {rejection}.");
			return;
		}

		using (OutPacket gauge = new OutPacket("GameTeamBoosterSetGaugePacket"))
		{
			gauge.WriteByte(requestedTeam);
			gauge.WriteFloat(broadcastGauge);
			foreach (SessionGroup target in targets)
			{
				target.Client.Send(gauge);
			}
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 TEAM BOOSTER] Added room={room.Id}, slot={slotId}, team={requestedTeam}, " +
			$"value={requestedValue:R}, players={teamPlayerCount}, contribution={contribution:R}, " +
			$"gauge={previousGauge:R}->{broadcastGauge:R}, completed={completed}, " +
			$"targets={targets.Count}.");
	}

	private static void HandleFlagTeamScoreRequest(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 1, "GameFlagRequestAddScorePacket");
		byte requestedTeam = packet.ReadByte();
		RequireConsumed(packet, "GameFlagRequestAddScorePacket");

		LegacyRoom room = LegacyRoomManager.GetFor(session);
		List<SessionGroup> targets = null;
		LegacyRoomMember member = null;
		uint redScore = 0;
		uint blueScore = 0;
		int raceGeneration = 0;
		List<SessionGroup> settlementTargets = null;
		List<int> settlementSlots = null;
		uint settlementEndTick = 0;
		uint settlementRedScore = 0;
		uint settlementBlueScore = 0;
		bool settlementPrepared = false;
		uint overtimeStartTick = 0;
		string rejection = string.Empty;
		if (room != null)
		{
			lock (room.SyncRoot)
			{
				member = FindMember(room, session);
				LegacyRaceParticipantSnapshot participant = member == null
					? null
					: FindRaceParticipantLocked(room, member.SlotId);
				if (room.GameType != 7)
				{
					rejection = $"game type {room.GameType} is not team flag mode";
				}
				else if (!room.Started || !room.StartSignalSent || room.RaceEnding)
				{
					rejection = "race is not accepting scores";
				}
				else if (room.FlagTeamOvertime &&
					IsTickBefore(
						unchecked((uint)Environment.TickCount64),
						room.FlagTeamOvertimeStartTick))
				{
					overtimeStartTick = room.FlagTeamOvertimeStartTick;
					rejection = $"overtime restart is pending until tick {overtimeStartTick}";
				}
				else if (member == null || !member.Initialized || !member.LoadedInGame ||
					participant == null || participant.Finished)
				{
					rejection = "sender is not an active loaded racer";
				}
				else if ((requestedTeam != 1 && requestedTeam != 2) ||
					requestedTeam != member.Team || requestedTeam != participant.Team)
				{
					rejection = $"team mismatch requested={requestedTeam}, member={member.Team}, " +
						$"race={participant.Team}";
				}
				else
				{
					if (requestedTeam == 1)
					{
						room.RedFlagScore = room.RedFlagScore == uint.MaxValue
							? uint.MaxValue
							: room.RedFlagScore + 1u;
					}
					else
					{
						room.BlueFlagScore = room.BlueFlagScore == uint.MaxValue
							? uint.MaxValue
							: room.BlueFlagScore + 1u;
					}

					redScore = room.RedFlagScore;
					blueScore = room.BlueFlagScore;
					raceGeneration = room.RaceGeneration;
					targets = GetRaceSessionsLocked(room, unfinishedOnly: false);
					// Serialize score broadcasts under the room lock. Two sessions may
					// score concurrently, and P236 must never observe the snapshots in
					// reverse order.
					BroadcastFlagTeamScore(targets, redScore, blueScore);
					if (room.FlagTeamOvertime && redScore != blueScore)
					{
						settlementPrepared = TryPrepareFlagTeamRaceSettlementLocked(
							room,
							raceGeneration,
							out settlementTargets,
							out settlementSlots,
							out settlementEndTick,
							out settlementRedScore,
							out settlementBlueScore);
					}
				}
			}
		}

		if (targets == null)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Rejected team score room={room?.Id ?? -1}, " +
				$"slot={member?.SlotId ?? -1}, requestedTeam={requestedTeam}: " +
				$"{(string.IsNullOrEmpty(rejection) ? "no active room" : rejection)}.");
			return;
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 FLAG] Added team score room={room.Id}, slot={member.SlotId}, " +
			$"team={requestedTeam}, red={redScore}, blue={blueScore}, targets={targets.Count}.");
		if (settlementPrepared)
		{
			DispatchFlagTeamRaceSettlement(
				room,
				raceGeneration,
				"overtime score",
				settlementTargets,
				settlementSlots,
				settlementEndTick,
				settlementRedScore,
				settlementBlueScore);
		}
	}

	private static void HandleGameControl(SessionGroup session, InPacket packet)
	{
		RequireAvailableAtLeast(packet, 13, "GameControlPacket");
		int state = packet.ReadInt();
		uint clientTickA = packet.ReadUInt();
		uint clientTickB = packet.ReadUInt();
		byte hasExtra = packet.ReadByte();
		string extraSummary = "none";
		if (hasExtra != 0)
		{
			// The optional proof occupies 24 bytes in the aligned client object,
			// but its byte-sized third field makes the wire representation 21
			// bytes: uint, uint, byte, uint, uint, uint.
			RequireAvailable(packet, 21, "GameControlPacket extra");
			uint proof0 = packet.ReadUInt();
			uint proof1 = packet.ReadUInt();
			byte proof2 = packet.ReadByte();
			uint proof3 = packet.ReadUInt();
			uint proof4 = packet.ReadUInt();
			uint proof5 = packet.ReadUInt();
			extraSummary =
				$"{proof0:X8}/{proof1:X8}/{proof2:X2}/{proof3:X8}/{proof4:X8}/{proof5:X8}";
		}
		RequireConsumed(packet, "GameControlPacket");

		if (state == 2)
		{
			HandleRaceFinish(session, clientTickA, clientTickB, extraSummary);
			return;
		}

		// State zero is the 2005 client's "course loaded" notification. The
		// two tick fields are uninitialized garbage in that outgoing shape and
		// must not participate in readiness or timing decisions.
		if (state != 0)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 GAME] Received unimplemented control state={state}, " +
				$"tickA={clientTickA}, tickB={clientTickB}, hasExtra={hasExtra}, " +
				$"roomId={session.Multiplayer.RoomId}.");
			return;
		}

		LegacyRoom room = LegacyRoomManager.GetFor(session);
		LegacyRoomMember member = null;
		bool duplicate = false;
		List<SessionGroup> startTargets = null;
		uint startTick = 0;
		int loadedCount = 0;
		int participantCount = 0;
		int raceGeneration = 0;
		bool lateObserverStart = false;
		uint observerRedScore = 0;
		uint observerBlueScore = 0;
		if (room != null)
		{
			lock (room.SyncRoot)
			{
				member = FindMember(room, session);
				if (room.Started && member != null && member.Initialized)
				{
					if (member.IsObserver)
					{
						duplicate = member.LoadedInGame;
						member.LoadedInGame = true;
						if (room.StartSignalSent)
						{
							lateObserverStart = true;
							startTick = room.StartTick;
							observerRedScore = room.RedFlagScore;
							observerBlueScore = room.BlueFlagScore;
						}
					}
					else if (Array.IndexOf(room.RaceGridSlots, member.SlotId) >= 0)
					{
						duplicate = member.LoadedInGame;
						member.LoadedInGame = true;
						TryPrepareGameStartSignalLocked(
							room,
							out startTargets,
							out startTick,
							out loadedCount,
							out participantCount);
						if (startTargets != null)
						{
							raceGeneration = room.RaceGeneration;
						}
					}
				}
			}
		}

		if (member == null || room == null || !room.Started)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 GAME] Ignored loaded signal outside an active race; " +
				$"roomId={room?.Id ?? -1}, memberFound={member != null}.");
			return;
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 GAME] Loaded room={room.Id}, slot={member.SlotId}, " +
			$"userNo={member.Session.Profile.UserNo}, duplicate={duplicate}, " +
			$"observer={member.IsObserver}, loaded={loadedCount}/{participantCount}.");

		if (lateObserverStart)
		{
			List<SessionGroup> observerTarget = new List<SessionGroup> { session };
			BroadcastGameStartSignal(room.Id, observerTarget, startTick);
			if (room.GameType == 7)
			{
				BroadcastFlagTeamScore(observerTarget, observerRedScore, observerBlueScore);
			}
			LegacyPacketTrace.LogEvent(
				$"[2005 OBSERVER] Synchronized late-loaded observer room={room.Id}, " +
				$"slot={member.SlotId}, startTick={startTick}.");
			return;
		}

		if (startTargets == null)
		{
			return;
		}

		BroadcastGameStartSignal(room.Id, startTargets, startTick);
		if (room.GameType == 7)
		{
			BroadcastFlagTeamScore(startTargets, 0, 0);
			_ = EndFlagTeamRaceAtDeadlineAsync(room, raceGeneration, startTick);
		}
		LegacyPacketTrace.LogEvent(
			$"[2005 GAME] All racers loaded room={room.Id}; state=1, " +
			$"startTick={startTick}, targets={startTargets.Count}.");
	}

	private static void HandleRaceFinish(
		SessionGroup session,
		uint finishTime,
		uint clientState,
		string proofSummary)
	{
		LegacyRoom room = LegacyRoomManager.GetFor(session);
		if (room?.GameType == 7)
		{
			HandleFlagTeamRaceFinish(room, session, finishTime, clientState, proofSummary);
			return;
		}

		LegacyRoomMember member = null;
		List<SessionGroup> raceTimeTargets = null;
		List<SessionGroup> countdownTargets = null;
		bool accepted = false;
		bool duplicate = false;
		bool firstFinisher = false;
		uint endTick = 0;
		uint resultMetric = finishTime;
		uint flagScore = 0;
		int raceGeneration = 0;
		if (room != null)
		{
			lock (room.SyncRoot)
			{
				member = FindMember(room, session);
				LegacyRaceParticipantSnapshot raceParticipant = member == null
					? null
					: FindRaceParticipantLocked(room, member.SlotId);
				if (room.Started && member != null && member.Initialized &&
					raceParticipant != null)
				{
					accepted = true;
					duplicate = raceParticipant.Finished;
					if (!duplicate)
					{
						if (room.GameType == 8)
						{
							flagScore = Math.Max(member.FlagScore, clientState);
							member.FlagScore = flagScore;
							raceParticipant.FlagScore = flagScore;
							resultMetric = flagScore;
						}
						raceParticipant.Finished = true;
						raceParticipant.FinishTime = finishTime;
						member.FinishedInGame = true;
						member.FinishTime = finishTime;
						raceTimeTargets = GetRaceSessionsLocked(room, unfinishedOnly: false);

						if (!room.RaceEnding)
						{
							firstFinisher = true;
							room.RaceEnding = true;
							room.RaceEndTick = unchecked((uint)Environment.TickCount64 + 10000u);
							endTick = room.RaceEndTick;
							raceGeneration = room.RaceGeneration;
							countdownTargets = GetRaceSessionsLocked(room, unfinishedOnly: true);
						}
					}
				}
			}
		}

		if (!accepted)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 GAME] Ignored finish outside an active race; " +
				$"roomId={room?.Id ?? -1}, memberFound={member != null}, time={finishTime}.");
			return;
		}

		if (duplicate)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 GAME] Ignored duplicate finish room={room.Id}, slot={member.SlotId}, " +
				$"userNo={member.Session.Profile.UserNo}, time={finishTime}.");
			return;
		}

		BroadcastRaceTime(
			raceTimeTargets,
			member.SlotId,
			resultMetric);
		LegacyPacketTrace.LogEvent(
			$"[2005 GAME] Finished room={room.Id}, slot={member.SlotId}, " +
			$"userNo={member.Session.Profile.UserNo}, time={finishTime}, " +
			$"clientState={clientState}, flagScore={flagScore}, resultMetric={resultMetric}, " +
			$"proof={proofSummary}, first={firstFinisher}.");

		if (!firstFinisher)
		{
			return;
		}

		BroadcastGameControl(countdownTargets, 3, endTick);
		LegacyPacketTrace.LogEvent(
			$"[2005 GAME] Finish countdown room={room.Id}, endTick={endTick}, " +
			$"unfinishedTargets={countdownTargets.Count}.");
		_ = SettleRaceAfterDelayAsync(room, raceGeneration);
	}

	private static void HandleFlagTeamRaceFinish(
		LegacyRoom room,
		SessionGroup session,
		uint finishTime,
		uint contributionCount,
		string proofSummary)
	{
		LegacyRoomMember member;
		int raceGeneration = 0;
		uint redScore = 0;
		uint blueScore = 0;
		uint elapsed = 0;
		List<SessionGroup> settlementTargets = null;
		List<int> settlementSlots = null;
		uint settlementEndTick = 0;
		bool settlementPrepared = false;
		string rejection = null;
		lock (room.SyncRoot)
		{
			member = FindMember(room, session);
			LegacyRaceParticipantSnapshot participant = member == null
				? null
				: FindRaceParticipantLocked(room, member.SlotId);
			uint now = unchecked((uint)Environment.TickCount64);
			int signedElapsed = room.StartTick == 0
				? 0
				: unchecked((int)(now - room.StartTick));
			elapsed = signedElapsed > 0 ? (uint)signedElapsed : 0;
			redScore = room.RedFlagScore;
			blueScore = room.BlueFlagScore;
			if (!room.Started || !room.StartSignalSent || member == null ||
				!member.Initialized || !member.LoadedInGame || participant == null)
			{
				rejection = "sender is not an active loaded racer";
			}
			else if (room.RaceEnding)
			{
				rejection = "team result settlement already started";
			}
			else if (redScore == blueScore)
			{
				rejection = "team score is tied; waiting for the server overtime restart";
			}
			else if (elapsed < FlagRaceMilliseconds)
			{
				rejection = $"regulation clock has only elapsed {elapsed} ms";
			}
			else
			{
				raceGeneration = room.RaceGeneration;
				settlementPrepared = TryPrepareFlagTeamRaceSettlementLocked(
					room,
					raceGeneration,
					out settlementTargets,
					out settlementSlots,
					out settlementEndTick,
					out redScore,
					out blueScore);
				if (!settlementPrepared)
				{
					rejection = "team result settlement could not be claimed";
				}
			}
		}

		if (rejection != null)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Ignored team finish room={room.Id}, slot={member?.SlotId ?? -1}, " +
				$"time={finishTime}, contributionCount={contributionCount}, elapsed={elapsed}, " +
				$"red={redScore}, blue={blueScore}, proof={proofSummary}: {rejection}.");
			return;
		}

		DispatchFlagTeamRaceSettlement(
			room,
			raceGeneration,
			$"client timeout from slot {member.SlotId}",
			settlementTargets,
			settlementSlots,
			settlementEndTick,
			redScore,
			blueScore);
		LegacyPacketTrace.LogEvent(
			$"[2005 FLAG] Team finish signal room={room.Id}, slot={member.SlotId}, " +
			$"time={finishTime}, contributionCount={contributionCount}, elapsed={elapsed}, " +
			$"red={redScore}, blue={blueScore}, proof={proofSummary}, settlementStarted=true.");
	}

	private static async Task EndFlagTeamRaceAtDeadlineAsync(
		LegacyRoom room,
		int raceGeneration,
		uint startTick)
	{
		uint now = unchecked((uint)Environment.TickCount64);
		uint untilStart = unchecked(startTick - now);
		if (untilStart > RaceStartCutsceneMilliseconds + RaceCountdownMilliseconds + 60000u)
		{
			untilStart = 0;
		}
		await Task.Delay(checked((int)(untilStart + FlagRaceMilliseconds))).ConfigureAwait(false);

		uint redScore = 0;
		uint blueScore = 0;
		uint overtimeStartTick = 0;
		List<SessionGroup> overtimeTargets = null;
		List<SessionGroup> settlementTargets = null;
		List<int> settlementSlots = null;
		uint settlementEndTick = 0;
		bool settlementPrepared = false;
		lock (room.SyncRoot)
		{
			if (!room.Started || !room.StartSignalSent || room.RaceEnding ||
				room.RaceGeneration != raceGeneration || room.GameType != 7)
			{
				return;
			}

			redScore = room.RedFlagScore;
			blueScore = room.BlueFlagScore;
			if (redScore == blueScore)
			{
				room.FlagTeamOvertime = true;
				// P236 does not enter sudden death from the local 180-second
				// timeout alone. GameControl state 6 is the team-flag-only draw
				// transition: it displays the native flag_draw sequence, changes
				// the regulation duration to UINT_MAX, and restarts the synchronized
				// countdown at tickA. Without this packet the client remains in its
				// end-of-regulation wait state and cannot race for the deciding flag.
				room.FlagOwnerSlots.Clear();
				overtimeStartTick = unchecked(
					(uint)Environment.TickCount64 +
					FlagOvertimeTransitionPaddingMilliseconds +
					RaceStartCutsceneMilliseconds +
					RaceCountdownMilliseconds);
				room.FlagTeamOvertimeStartTick = overtimeStartTick;
				overtimeTargets = GetRaceSessionsLocked(room, unfinishedOnly: false);
			}
			else
			{
				settlementPrepared = TryPrepareFlagTeamRaceSettlementLocked(
					room,
					raceGeneration,
					out settlementTargets,
					out settlementSlots,
					out settlementEndTick,
					out redScore,
					out blueScore);
			}
		}

		if (redScore == blueScore)
		{
			BroadcastGameControl(overtimeTargets, 6, overtimeStartTick);
			LegacyPacketTrace.LogEvent(
				$"[2005 FLAG] Team regulation tied room={room.Id}, " +
				$"red={redScore}, blue={blueScore}; sent GameControl state=6 " +
				$"overtimeStartTick={overtimeStartTick}, targets={overtimeTargets.Count}.");
			return;
		}

		if (settlementPrepared)
		{
			DispatchFlagTeamRaceSettlement(
				room,
				raceGeneration,
				"regulation timeout",
				settlementTargets,
				settlementSlots,
				settlementEndTick,
				redScore,
				blueScore);
		}
	}

	private static bool TryPrepareFlagTeamRaceSettlementLocked(
		LegacyRoom room,
		int raceGeneration,
		out List<SessionGroup> targets,
		out List<int> finishedSlots,
		out uint endTick,
		out uint redScore,
		out uint blueScore)
	{
		targets = null;
		finishedSlots = null;
		endTick = 0;
		redScore = room.RedFlagScore;
		blueScore = room.BlueFlagScore;
		if (!room.Started || !room.StartSignalSent || room.RaceEnding ||
			room.RaceGeneration != raceGeneration || room.GameType != 7 ||
			redScore == blueScore)
		{
			return false;
		}

		finishedSlots = new List<int>();
		foreach (LegacyRaceParticipantSnapshot participant in room.RaceParticipants)
		{
			participant.Finished = true;
			participant.FinishTime = FlagRaceMilliseconds;
			if (participant.SlotId >= 0 && participant.SlotId < room.Members.Length)
			{
				LegacyRoomMember member = room.Members[participant.SlotId];
				if (member != null && member.Initialized)
				{
					member.FinishedInGame = true;
					member.FinishTime = FlagRaceMilliseconds;
					finishedSlots.Add(participant.SlotId);
				}
			}
		}

		room.FlagOwnerSlots.Clear();
		room.FlagTeamOvertime = false;
		room.FlagTeamOvertimeStartTick = 0;
		room.RaceEnding = true;
		room.RaceEndTick = unchecked((uint)Environment.TickCount64 + 10000u);
		endTick = room.RaceEndTick;
		targets = GetRaceSessionsLocked(room, unfinishedOnly: false);
		return true;
	}

	private static void DispatchFlagTeamRaceSettlement(
		LegacyRoom room,
		int raceGeneration,
		string reason,
		IReadOnlyList<SessionGroup> targets,
		IReadOnlyList<int> finishedSlots,
		uint endTick,
		uint redScore,
		uint blueScore)
	{
		foreach (int slotId in finishedSlots)
		{
			BroadcastRaceTime(targets, slotId, FlagRaceMilliseconds);
		}
		BroadcastGameControl(targets, 3, endTick);
		LegacyPacketTrace.LogEvent(
			$"[2005 FLAG] Team finish room={room.Id}, reason={reason}, " +
			$"red={redScore}, blue={blueScore}, endTick={endTick}, " +
			$"racers={finishedSlots.Count}, targets={targets.Count}.");
		_ = SettleRaceAfterDelayAsync(room, raceGeneration);
	}

	private static async Task SettleRaceAfterDelayAsync(LegacyRoom room, int raceGeneration)
	{
		await Task.Delay(10000).ConfigureAwait(false);

		List<SessionGroup> targets;
		List<LegacyRaceResultEntry> results;
		byte winnerTeam;
		uint resultTick;
		string grid;
		int previousOwnerSlot;
		int nextOwnerSlot;
		string nextOwnerPolicy;
		lock (room.SyncRoot)
		{
			if (!room.Started || !room.RaceEnding || room.RaceGeneration != raceGeneration)
			{
				return;
			}

			targets = GetRaceSessionsLocked(room, unfinishedOnly: false);
			results = BuildRaceResultsLocked(room);
			winnerTeam = DetermineWinnerTeamLocked(room, results);
			resultTick = unchecked(room.RaceEndTick + 6000u);
			previousOwnerSlot = room.OwnerSlot;
			ApplyFinishOrderToGridLocked(room);
			int observerOwnerSlot = FindPreferredObserverOwnerSlotLocked(
				room,
				previousOwnerSlot);
			if (observerOwnerSlot >= 0)
			{
				room.OwnerSlot = observerOwnerSlot;
				nextOwnerPolicy = "observer";
			}
			else
			{
				nextOwnerPolicy = "existing";
				// Without an observer, hand authority to the best still-connected
				// finisher. The immutable result snapshot can also contain riders who
				// left during the countdown.
				foreach (LegacyRaceResultEntry result in results)
				{
					if (result.SlotId >= 0 && result.SlotId < room.Members.Length &&
						room.Members[result.SlotId] != null)
					{
						room.OwnerSlot = result.SlotId;
						nextOwnerPolicy = "winner";
						break;
					}
				}
			}
			nextOwnerSlot = room.OwnerSlot;
			grid = DescribeRaceGridLocked(room);

			room.Started = false;
			room.StartSignalSent = false;
			room.StartTick = 0;
			room.RaceEnding = false;
			room.RaceEndTick = 0;
			room.RaceGridSlots = Array.Empty<int>();
			room.RaceParticipants = Array.Empty<LegacyRaceParticipantSnapshot>();
			room.RedTeamBoosterGauge = 0f;
			room.BlueTeamBoosterGauge = 0f;
			room.FlagOwnerSlots.Clear();
			room.RedFlagScore = 0;
			room.BlueFlagScore = 0;
			room.FlagTeamOvertime = false;
			room.FlagTeamOvertimeStartTick = 0;
			foreach (LegacyRoomMember racer in room.Members)
			{
				if (racer == null)
				{
					continue;
				}

				racer.LoadedInGame = false;
				racer.FlagScore = 0;
				racer.Status = racer.IsObserver ? 4 : 2;
			}
		}

		BroadcastGameResult(targets, results, winnerTeam);
		// Store the result snapshot before advancing the client's hard-coded
		// result deadline. Without a GameResultPacket, state 4 stalls permanently.
		BroadcastGameControl(targets, 4, resultTick);
		LegacyPacketTrace.LogEvent(
			$"[2005 GAME] Settled race room={room.Id}, resultTick={resultTick}, " +
			$"targets={targets.Count}, results={results.Count}, winnerTeam={winnerTeam}, " +
			$"ownerSlot={previousOwnerSlot}->{nextOwnerSlot}, ownerPolicy={nextOwnerPolicy}, " +
			$"nextGrid=[{grid}].");
	}

	private static List<LegacyRaceResultEntry> BuildRaceResultsLocked(LegacyRoom room)
	{
		List<LegacyRaceParticipantSnapshot> racers =
			new List<LegacyRaceParticipantSnapshot>(room.RaceParticipants);
		byte flagWinnerTeam = room.GameType == 7
			? room.RedFlagScore > room.BlueFlagScore ? (byte)1 : (byte)2
			: (byte)0;
		racers.Sort((left, right) =>
		{
			if (room.GameType == 8)
			{
				int byFlagScore = right.FlagScore.CompareTo(left.FlagScore);
				return byFlagScore != 0
					? byFlagScore
					// P236's live personal-flag ranking uses the stable kart
					// identity in descending order when possession time is tied.
					: right.SlotId.CompareTo(left.SlotId);
			}

			if (room.GameType == 7)
			{
				int byWinningTeam = (right.Team == flagWinnerTeam).CompareTo(
					left.Team == flagWinnerTeam);
				return byWinningTeam != 0
					? byWinningTeam
					: left.GridPosition.CompareTo(right.GridPosition);
			}

			int byFinished = right.Finished.CompareTo(left.Finished);
			if (byFinished != 0)
			{
				return byFinished;
			}

			int byTime = left.FinishTime.CompareTo(right.FinishTime);
			return byTime != 0 ? byTime : left.GridPosition.CompareTo(right.GridPosition);
		});

		List<LegacyRaceResultEntry> results = new List<LegacyRaceResultEntry>(racers.Count);
		for (int rank = 0; rank < racers.Count; rank++)
		{
			LegacyRaceParticipantSnapshot racer = racers[rank];
			int points = IsTeamGameType(room.GameType) && room.GameType != 7 && racer.Finished
				? GetRankPoints(rank)
				: 0;
			results.Add(new LegacyRaceResultEntry
			{
				SlotId = racer.SlotId,
				FinishTime = room.GameType == 8
					? racer.FlagScore
					: racer.Finished ? racer.FinishTime : uint.MaxValue,
				Rank = rank,
				Team = IsTeamGameType(room.GameType) ? racer.Team : (byte)0,
				Points = points
			});
		}

		return results;
	}

	private static LegacyRaceParticipantSnapshot FindRaceParticipantLocked(
		LegacyRoom room,
		int slotId)
	{
		foreach (LegacyRaceParticipantSnapshot participant in room.RaceParticipants)
		{
			if (participant.SlotId == slotId)
			{
				return participant;
			}
		}

		return null;
	}

	private static int CountActiveRaceTeamMembersLocked(LegacyRoom room, byte team)
	{
		int count = 0;
		foreach (int slot in room.RaceGridSlots)
		{
			if (slot < 0 || slot >= room.Members.Length)
			{
				continue;
			}

			LegacyRoomMember member = room.Members[slot];
			if (member != null && member.Initialized && member.Team == team &&
				FindRaceParticipantLocked(room, slot)?.Team == team)
			{
				count++;
			}
		}

		return count;
	}

	private static List<SessionGroup> GetActiveRaceTeamSessionsLocked(
		LegacyRoom room,
		byte team)
	{
		List<SessionGroup> sessions = new List<SessionGroup>();
		foreach (int slot in room.RaceGridSlots)
		{
			if (slot < 0 || slot >= room.Members.Length)
			{
				continue;
			}

			LegacyRoomMember member = room.Members[slot];
			if (member != null && member.Initialized && member.Team == team &&
				FindRaceParticipantLocked(room, slot)?.Team == team)
			{
				sessions.Add(member.Session);
			}
		}

		foreach (LegacyRoomMember observer in room.Members)
		{
			if (observer != null && observer.IsObserver && observer.Initialized &&
				observer.LoadedInGame)
			{
				sessions.Add(observer.Session);
			}
		}

		return sessions;
	}

	private static int GetRankPoints(int rank)
	{
		int[] rankPoints = { 10, 8, 6, 5, 4, 3, 2, 1 };
		return rank >= 0 && rank < rankPoints.Length ? rankPoints[rank] : 0;
	}

	private static byte DetermineWinnerTeamLocked(
		LegacyRoom room,
		IReadOnlyList<LegacyRaceResultEntry> results)
	{
		if (!IsTeamGameType(room.GameType))
		{
			return 0;
		}

		if (room.GameType == 7)
		{
			return room.RedFlagScore == room.BlueFlagScore
				? (byte)0
				: room.RedFlagScore > room.BlueFlagScore ? (byte)1 : (byte)2;
		}

		byte firstFinisherTeam = 0;
		foreach (LegacyRaceResultEntry entry in results)
		{
			if (entry.FinishTime != uint.MaxValue && (entry.Team == 1 || entry.Team == 2))
			{
				firstFinisherTeam = entry.Team;
				break;
			}
		}

		// Item-team races are decided by the first finisher. Speed-team races use
		// the classic 10/8/6/5/4/3/2/1 rank score, with the first finisher as the
		// tie breaker. Rider-school aliases 5/6 follow their respective rules.
		if (room.GameType == 4 || room.GameType == 6)
		{
			return firstFinisherTeam;
		}

		int redScore = 0;
		int blueScore = 0;
		foreach (LegacyRaceResultEntry entry in results)
		{
			if (entry.Team == 1)
			{
				redScore += entry.Points;
			}
			else if (entry.Team == 2)
			{
				blueScore += entry.Points;
			}
		}

		return redScore == blueScore
			? firstFinisherTeam
			: redScore > blueScore ? (byte)1 : (byte)2;
	}

	private static List<SessionGroup> GetRaceSessionsLocked(
		LegacyRoom room,
		bool unfinishedOnly)
	{
		List<SessionGroup> sessions = new List<SessionGroup>();
		foreach (int slot in room.RaceGridSlots)
		{
			if (slot < 0 || slot >= room.Members.Length)
			{
				continue;
			}

			LegacyRoomMember racer = room.Members[slot];
			if (racer != null && racer.Initialized &&
				(!unfinishedOnly || !racer.FinishedInGame))
			{
				sessions.Add(racer.Session);
			}
		}

		// Observers receive every race-wide state update but never participate in
		// finish, rank, team, or readiness calculations.
		foreach (LegacyRoomMember observer in room.Members)
		{
			if (observer != null && observer.IsObserver && observer.Initialized &&
				observer.LoadedInGame)
			{
				sessions.Add(observer.Session);
			}
		}

		return sessions;
	}

	private static void ApplyFinishOrderToGridLocked(LegacyRoom room)
	{
		List<LegacyRoomMember> racers = GetFinishOrderLocked(room);
		for (int position = 0; position < racers.Count; position++)
		{
			racers[position].GridPosition = checked((byte)position);
		}
	}

	private static List<LegacyRoomMember> GetFinishOrderLocked(LegacyRoom room)
	{
		List<LegacyRoomMember> racers = new List<LegacyRoomMember>();
		foreach (int slot in room.RaceGridSlots)
		{
			if (slot >= 0 && slot < room.Members.Length && room.Members[slot] != null)
			{
				racers.Add(room.Members[slot]);
			}
		}

		racers.Sort((left, right) =>
		{
			if (room.GameType == 8)
			{
				int byFlagScore = right.FlagScore.CompareTo(left.FlagScore);
				return byFlagScore != 0
					? byFlagScore
					: right.SlotId.CompareTo(left.SlotId);
			}

			if (room.GameType == 7)
			{
				byte winnerTeam = room.RedFlagScore > room.BlueFlagScore ? (byte)1 : (byte)2;
				int byWinningTeam = (right.Team == winnerTeam).CompareTo(left.Team == winnerTeam);
				return byWinningTeam != 0
					? byWinningTeam
					: left.GridPosition.CompareTo(right.GridPosition);
			}

			int byFinished = right.FinishedInGame.CompareTo(left.FinishedInGame);
			if (byFinished != 0)
			{
				return byFinished;
			}

			int byTime = left.FinishTime.CompareTo(right.FinishTime);
			return byTime != 0 ? byTime : left.GridPosition.CompareTo(right.GridPosition);
		});
		return racers;
	}

	private static void BroadcastRaceTime(
		IReadOnlyList<SessionGroup> targets,
		int slotId,
		uint finishTime)
	{
		using OutPacket raceTime = new OutPacket("GameRaceTimePacket");
		// The client-side GoKart identity is the stable physical room slot,
		// not the account's UserNo.
		raceTime.WriteInt(slotId);
		raceTime.WriteUInt(finishTime);
		foreach (SessionGroup target in targets)
		{
			target.Client.Send(raceTime);
		}
	}

	private static void BroadcastFlagTeamScore(
		IReadOnlyList<SessionGroup> targets,
		uint redScore,
		uint blueScore)
	{
		using OutPacket score = new OutPacket("GameFlagSetScorePacket");
		score.WriteUInt(redScore);
		score.WriteUInt(blueScore);
		foreach (SessionGroup target in targets)
		{
			target.Client.Send(score);
		}
	}

	private static void BroadcastGameControl(
		IReadOnlyList<SessionGroup> targets,
		int state,
		uint tick)
	{
		using OutPacket control = new OutPacket("GameControlPacket");
		control.WriteInt(state);
		control.WriteUInt(tick);
		control.WriteUInt(0);
		control.WriteByte(0);
		foreach (SessionGroup target in targets)
		{
			target.Client.Send(control);
		}
	}

	private static void BroadcastGameResult(
		IReadOnlyList<SessionGroup> targets,
		IReadOnlyList<LegacyRaceResultEntry> results,
		byte winnerTeam)
	{
		using OutPacket result = new OutPacket("GameResultPacket");
		result.WriteByte(winnerTeam);
		result.WriteInt(results.Count);
		foreach (LegacyRaceResultEntry entry in results)
		{
			// The P236 result record is exactly 66 wire bytes. Team stages consume
			// the dword at offset 36 as rank points and the byte at offset 40 as the
			// 1/2 team ID; the remaining progression/effect fields stay zero.
			result.WriteInt(entry.SlotId);
			result.WriteUInt(entry.FinishTime);
			result.WriteInt(entry.Rank);
			result.WriteBytes(new byte[24]);
			result.WriteInt(entry.Points);
			result.WriteByte(entry.Team);
			result.WriteBytes(new byte[25]);
		}
		result.WriteByte(0);
		result.WriteUInt(0);
		result.WriteUInt(0);
		result.WriteUInt(0);

		foreach (SessionGroup target in targets)
		{
			target.Client.Send(result);
		}
	}

	private static bool TryPrepareGameStartSignalLocked(
		LegacyRoom room,
		out List<SessionGroup> targets,
		out uint startTick,
		out int loadedCount,
		out int participantCount)
	{
		targets = null;
		startTick = room.StartTick;
		loadedCount = 0;
		participantCount = 0;

		foreach (int slot in room.RaceGridSlots)
		{
			if (slot < 0 || slot >= room.Members.Length)
			{
				continue;
			}

			LegacyRoomMember member = room.Members[slot];
			if (member == null || !member.Initialized)
			{
				continue;
			}

			participantCount++;
			if (member.LoadedInGame)
			{
				loadedCount++;
			}
		}

		if (!room.Started || room.StartSignalSent || participantCount == 0 ||
			loadedCount != participantCount)
		{
			return false;
		}

		room.StartSignalSent = true;
		// P236 begins showing 3 when the absolute start tick is within three
		// seconds. P236's local race state machines reserve exactly four seconds
		// for the start-camera cutscene, then let the
		// client run its native synchronized 3-2-1 countdown.
		room.StartTick = unchecked(
			(uint)Environment.TickCount64 +
			RaceStartCutsceneMilliseconds +
			RaceCountdownMilliseconds);
		startTick = room.StartTick;
		targets = GetRaceSessionsLocked(room, unfinishedOnly: false);
		return true;
	}

	private static void BroadcastGameStartSignal(
		int roomId,
		IReadOnlyList<SessionGroup> targets,
		uint startTick)
	{
		using OutPacket control = new OutPacket("GameControlPacket");
		control.WriteInt(1);
		control.WriteUInt(startTick);
		control.WriteUInt(0);
		control.WriteByte(0);
		foreach (SessionGroup target in targets)
		{
			target.Client.Send(control);
		}
	}

	private static string DescribeRaceGridLocked(LegacyRoom room)
	{
		List<string> entries = new List<string>();
		foreach (int slot in room.RaceGridSlots)
		{
			LegacyRoomMember member = room.Members[slot];
			if (member != null)
			{
				entries.Add(
					$"grid{member.GridPosition}=slot{slot}/user{member.Session.Profile.UserNo}/team{member.Team}");
			}
		}
		return string.Join(", ", entries);
	}

	private static void HandleSetRiderItemOn(SessionGroup session, InPacket packet)
	{
		RequireAvailable(packet, 18, "LoRqSetRiderItemOnPacket");
		ushort character = packet.ReadUShort();
		ushort paint = packet.ReadUShort();
		ushort kart = packet.ReadUShort();
		ushort plate = packet.ReadUShort();
		ushort goggle = packet.ReadUShort();
		ushort balloon = packet.ReadUShort();
		ushort reserved0 = packet.ReadUShort();
		ushort headBand = packet.ReadUShort();
		ushort reserved1 = packet.ReadUShort();
		RequireConsumed(packet, "LoRqSetRiderItemOnPacket");

		session.Profile.Equipment.Character = character;
		session.Profile.Equipment.Paint = paint;
		session.Profile.Equipment.Kart = kart;
		session.Profile.Equipment.Plate = plate;
		session.Profile.Equipment.Goggle = goggle;
		session.Profile.Equipment.Balloon = balloon;
		session.Profile.Equipment.Reserved0 = reserved0;
		session.Profile.Equipment.HeadBand = headBand;
		session.Profile.Equipment.Reserved1 = reserved1;
		RouterListener.SaveProfile(session);
		LegacyRoom room = LegacyRoomManager.GetFor(session);
		if (room != null)
		{
			// There is no GrSlotItemOnPacket in the 2005 client. Re-sending the
			// canonical full slot packet both refreshes the local preview and is
			// the format remote riders consume for equipment changes.
			BroadcastSlotData(room);
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Equipped character={character}, paint={paint}, kart={kart}, " +
			$"plate={plate}, goggle={goggle}, balloon={balloon}, headBand={headBand}, " +
			$"reserved=({reserved0},{reserved1}), roomId={room?.Id ?? -1}.");
	}

	private static void SendSlotData(SessionGroup session, LegacyRoom room)
	{
		OutPacket reply = new OutPacket("GrSlotDataPacket");
		int populatedSlots;
		lock (room.SyncRoot)
		{
			populatedSlots = WriteSlotDataBody(reply, room);
		}

		using (reply)
		{
			session.Client.Send(reply);
		}
		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Sent room id={room.Id} slots to one client; populatedSlots={populatedSlots}.");
	}

	private static void BroadcastSlotData(LegacyRoom room)
	{
		OutPacket packet = new OutPacket("GrSlotDataPacket");
		List<SessionGroup> targets;
		int populatedSlots;
		lock (room.SyncRoot)
		{
			targets = GetMemberSessions(room, initializedOnly: true);
			populatedSlots = WriteSlotDataBody(packet, room);
		}

		using (packet)
		{
			foreach (SessionGroup target in targets)
			{
				target.Client.Send(packet);
			}
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 MP] Broadcast room id={room.Id} slots to {targets.Count} initialized clients; " +
			$"populatedSlots={populatedSlots}.");
	}

	private static void WriteSessionDataBody(OutPacket packet, LegacyRoom room)
	{
		packet.WriteString(room.Name);
		packet.WriteByte(room.GameType);
		packet.WriteInt(room.Unknown1);
		// P236 compares this byte with the number of active rider slots while it
		// waits for the room/race snapshot. It is not a speed-type field.
		packet.WriteByte(checked((byte)LegacyRoomManager.CountInitializedRiders(room)));
		packet.WriteInt(room.Unknown2);
	}

	private static int WriteSlotDataBody(
		OutPacket packet,
		LegacyRoom room,
		bool forRaceStart = false)
	{
		packet.WriteUInt(room.Track);
		packet.WriteBytes(room.TrackCandidates);
		packet.WriteUInt(unchecked((uint)room.OwnerSlot));

		int populatedSlots = 0;
		int riderSlotCount = Math.Min(8, room.Members.Length);
		for (int slot = 0; slot < riderSlotCount; slot++)
		{
			LegacyRoomMember member = room.Members[slot];
			if (member == null || member.IsObserver || !member.Initialized)
			{
				packet.WriteInt(0);
				continue;
			}

			WriteMemberRiderSlot(
				packet,
				member,
				forRaceStart ? 3 : member.Status,
				IsTeamGameType(room.GameType) ? member.Team : (byte)0);
			populatedSlots++;
		}

		for (int slot = riderSlotCount; slot < room.Members.Length; slot++)
		{
			LegacyRoomMember member = room.Members[slot];
			if (member == null || !member.IsObserver || !member.Initialized)
			{
				packet.WriteInt(0);
				continue;
			}

			WriteObserverSlot(packet, member);
			populatedSlots++;
		}

		return populatedSlots;
	}

	private static LegacyRoomMember FindMember(LegacyRoom room, SessionGroup session)
	{
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member != null && ReferenceEquals(member.Session, session))
			{
				return member;
			}
		}

		return null;
	}

	private static int FindPreferredObserverOwnerSlotLocked(
		LegacyRoom room,
		int preferredSlot)
	{
		if (preferredSlot >= 0 && preferredSlot < room.Members.Length &&
			room.Members[preferredSlot]?.IsObserver == true)
		{
			return preferredSlot;
		}

		LegacyRoomMember latestObserver = null;
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member == null || !member.IsObserver)
			{
				continue;
			}

			// Entering observers take authority, so the most recently joined
			// remaining observer is the natural successor if the prior observer
			// owner disconnected during the race.
			if (latestObserver == null ||
				member.JoinSequence > latestObserver.JoinSequence ||
				(member.JoinSequence == latestObserver.JoinSequence &&
					member.SlotId > latestObserver.SlotId))
			{
				latestObserver = member;
			}
		}

		return latestObserver?.SlotId ?? -1;
	}

	private static bool IsOwner(LegacyRoom room, SessionGroup session)
	{
		return room.OwnerSlot >= 0 &&
			room.OwnerSlot < room.Members.Length &&
			room.Members[room.OwnerSlot] != null &&
			ReferenceEquals(room.Members[room.OwnerSlot].Session, session);
	}

	private static List<SessionGroup> GetMemberSessions(LegacyRoom room, bool initializedOnly)
	{
		List<SessionGroup> sessions = new List<SessionGroup>();
		foreach (LegacyRoomMember member in room.Members)
		{
			if (member != null && (!initializedOnly || member.Initialized))
			{
				sessions.Add(member.Session);
			}
		}

		return sessions;
	}

	private static void WriteMemberRiderSlot(
		OutPacket packet,
		LegacyRoomMember member,
		int wireStatus,
		byte wireTeam)
	{
		SessionGroup session = member.Session;
		IPEndPoint primaryEndPoint = ResolvePrimaryUdpEndPoint(session);
		IPEndPoint secondaryEndPoint = ResolveSecondaryUdpEndPoint(session, primaryEndPoint);
		WriteRiderSlot(
			packet,
			wireStatus,
			session.Profile.UserNo,
			primaryEndPoint,
			secondaryEndPoint,
			session.Profile.Nickname,
			session.Profile.LicenseLevel,
			session.Profile.GetLicenseCompletionMasks(),
			session.Profile.Equipment.Character,
			session.Profile.Equipment.Paint,
			session.Profile.Equipment.Kart,
			session.Profile.Equipment.Plate,
			session.Profile.Equipment.Goggle,
			session.Profile.Equipment.Balloon,
			session.Profile.Equipment.Reserved0,
			session.Profile.Equipment.HeadBand,
			session.Profile.Equipment.Reserved1,
			session.Profile.RiderIntro,
			session.Profile.RP,
			wireTeam,
			member.GridPosition,
			session.Profile.PMap,
			0);
	}

	private static void WriteObserverSlot(OutPacket packet, LegacyRoomMember member)
	{
		SessionGroup session = member.Session;
		IPEndPoint primaryEndPoint = ResolvePrimaryUdpEndPoint(session);
		IPEndPoint secondaryEndPoint = ResolveSecondaryUdpEndPoint(session, primaryEndPoint);
		packet.WriteInt(4);
		packet.WriteUInt(session.Profile.UserNo);
		packet.WriteEndPoint(primaryEndPoint);
		packet.WriteEndPoint(secondaryEndPoint);
		packet.WriteString(session.Profile.Nickname);
	}

	private static void WriteRiderSlot(
		OutPacket packet,
		int status,
		uint userNo,
		IPEndPoint primaryEndPoint,
		IPEndPoint secondaryEndPoint,
		string nickname,
		byte license,
		IReadOnlyList<ushort> completionMasks,
		ushort character,
		ushort paint,
		ushort kart,
		ushort plate,
		ushort goggle,
		ushort balloon,
		ushort reserved0,
		ushort headBand,
		ushort reserved1,
		string intro,
		int rp,
		byte team,
		byte gridPosition,
		uint pmap,
		uint gp)
	{
		if (status != 2 && status != 3 && status != 5)
		{
			throw new ArgumentOutOfRangeException(nameof(status), "A full 2005 rider slot requires status 2, 3, or 5.");
		}

		if (completionMasks == null || completionMasks.Count != 6)
		{
			throw new ArgumentException("A 2005 rider slot requires exactly six completion masks.", nameof(completionMasks));
		}

		if (team > 2)
		{
			throw new ArgumentOutOfRangeException(nameof(team), "A 2005 rider team must be 0, 1 (red), or 2 (blue).");
		}

		packet.WriteInt(status);
		packet.WriteUInt(userNo);
		packet.WriteEndPoint(primaryEndPoint);
		packet.WriteEndPoint(secondaryEndPoint);
		packet.WriteString(nickname);
		packet.WriteByte(license);
		foreach (ushort completionMask in completionMasks)
		{
			packet.WriteUShort(completionMask);
		}

		// Three emblem fields.
		for (int emblem = 0; emblem < 3; emblem++)
		{
			packet.WriteUShort(0);
		}

		// Character, paint, kart, plate, goggle, balloon, reserved, headband,
		// reserved. The two reserved equipment words are part of the 2005 shape.
		packet.WriteUShort(character);
		packet.WriteUShort(paint);
		packet.WriteUShort(kart);
		packet.WriteUShort(plate);
		packet.WriteUShort(goggle);
		packet.WriteUShort(balloon);
		packet.WriteUShort(reserved0);
		packet.WriteUShort(headBand);
		packet.WriteUShort(reserved1);

		packet.WriteString(intro);
		packet.WriteInt(rp);
		packet.WriteByte(team);
		packet.WriteByte(gridPosition); // previous rank / race starting grid
		packet.WriteUInt(pmap);
		packet.WriteUInt(gp);
	}

	private static IPEndPoint ResolvePrimaryUdpEndPoint(SessionGroup session)
	{
		if (IsUsableEndPoint(session.Multiplayer.ObservedUdpEndPoint))
		{
			return session.Multiplayer.ObservedUdpEndPoint;
		}

		IPEndPoint reportedEndPoint = session.Multiplayer.ReportedUdpEndPoint;
		if (IsUsableEndPoint(reportedEndPoint))
		{
			// Match the legacy server's P2P convention: use the address observed by
			// the TCP server with the UDP port reported by the client. This remains
			// reachable when the client reports a private LAN address.
			if (session.Client.Socket.RemoteEndPoint is IPEndPoint remoteEndPoint &&
				!remoteEndPoint.Address.Equals(IPAddress.Any) &&
				!remoteEndPoint.Address.Equals(IPAddress.IPv6Any))
			{
				return new IPEndPoint(remoteEndPoint.Address, reportedEndPoint.Port);
			}

			return reportedEndPoint;
		}

		throw new InvalidOperationException(
			"Cannot send an active 2005 room slot before the client's UDP endpoint is known.");
	}

	private static IPEndPoint ResolveSecondaryUdpEndPoint(
		SessionGroup session,
		IPEndPoint primaryEndPoint)
	{
		IPEndPoint reportedEndPoint = session.Multiplayer.ReportedUdpEndPoint;
		if (IsUsableEndPoint(reportedEndPoint) && !reportedEndPoint.Equals(primaryEndPoint))
		{
			return reportedEndPoint;
		}

		return new IPEndPoint(IPAddress.Any, 0);
	}

	private static bool IsUsableEndPoint(IPEndPoint endPoint)
	{
		return endPoint != null &&
			endPoint.Port != 0 &&
			!endPoint.Address.Equals(IPAddress.Any) &&
			!endPoint.Address.Equals(IPAddress.IPv6Any);
	}

	private static bool IsTickBefore(uint tick, uint deadline)
	{
		// Environment.TickCount wraps every ~49.7 days. Signed subtraction keeps
		// comparisons correct as long as the deadline is less than 24.8 days away;
		// every synchronized race deadline here is only seconds or minutes away.
		return unchecked((int)(tick - deadline)) < 0;
	}

	private static void RequireAvailable(InPacket packet, int expected, string packetName)
	{
		if (packet.Available != expected)
		{
			throw new InvalidOperationException(
				$"{packetName} body length mismatch: expected {expected}, got {packet.Available}.");
		}
	}

	private static void RequireAvailableAtLeast(InPacket packet, int minimum, string packetName)
	{
		if (packet.Available < minimum)
		{
			throw new InvalidOperationException(
				$"{packetName} body length mismatch: expected at least {minimum}, got {packet.Available}.");
		}
	}

	private static void RequireConsumed(InPacket packet, string packetName)
	{
		if (packet.Available != 0)
		{
			throw new InvalidOperationException(
				$"{packetName} left {packet.Available} unread body bytes.");
		}
	}

	private static uint Hash(string packetName)
	{
		return Adler32Helper.GenerateAdler32_ASCII(packetName);
	}

	private static bool IsTeamGameType(byte gameType)
	{
		return gameType == 3 || gameType == 4 || gameType == 5 || gameType == 6 ||
			gameType == 7;
	}

	private static bool IsSpeedTeamGameType(byte gameType)
	{
		return gameType == 3 || gameType == 5;
	}

	private static bool IsItemEnabledGameType(byte gameType)
	{
		return gameType == 2 || gameType == 4 || gameType == 6 ||
			gameType == 7 || gameType == 8;
	}
}
