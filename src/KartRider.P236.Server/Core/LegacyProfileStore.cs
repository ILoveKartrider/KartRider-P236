#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KartRider.P236.Server;

namespace KartRider;

internal interface ILegacyProfileStore
{
	IReadOnlyList<LegacyProfileRecord> LoadAll();

	void Save(LegacyProfileRecord profile);

	void SaveAll(IEnumerable<LegacyProfileRecord> profiles);
}

internal sealed class LegacyProfileRecord
{
	public string SourceUsername { get; set; } = string.Empty;

	public string UserId { get; set; } = string.Empty;

	public string Nickname { get; set; } = string.Empty;

	public uint UserNo { get; set; }

	public byte LicenseLevel { get; set; }

	public ushort[] LicenseCompletionMasks { get; set; } = Array.Empty<ushort>();

	public int RP { get; set; }

	public uint PMap { get; set; }

	public uint Lucci { get; set; }

	public short SlotChanger { get; set; }

	public string RiderIntro { get; set; } = string.Empty;

	public LegacyEquipmentRecord Equipment { get; set; } = new LegacyEquipmentRecord();

	public static LegacyProfileRecord FromProfile(LegacySessionProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		return new LegacyProfileRecord
		{
			SourceUsername = profile.SourceUsername ?? string.Empty,
			UserId = profile.UserId,
			Nickname = profile.Nickname,
			UserNo = profile.UserNo,
			LicenseLevel = profile.LicenseLevel,
			LicenseCompletionMasks = profile.GetLicenseCompletionMasks(),
			RP = profile.RP,
			PMap = profile.PMap,
			Lucci = profile.Lucci,
			SlotChanger = profile.SlotChanger,
			RiderIntro = profile.RiderIntro,
			Equipment = LegacyEquipmentRecord.FromEquipment(profile.Equipment)
		};
	}

	public LegacySessionProfile ToProfile()
	{
		return LegacySessionProfile.CreatePersisted(
			SourceUsername,
			UserNo,
			UserId,
			Nickname,
			RiderIntro,
			RP,
			PMap,
			Lucci,
			SlotChanger,
			LicenseLevel,
			LicenseCompletionMasks,
			Equipment.ToEquipment());
	}

	public LegacyProfileRecord Clone()
	{
		return new LegacyProfileRecord
		{
			SourceUsername = SourceUsername,
			UserId = UserId,
			Nickname = Nickname,
			UserNo = UserNo,
			LicenseLevel = LicenseLevel,
			LicenseCompletionMasks =
				LicenseCompletionMasks == null
					? null
					: (ushort[])LicenseCompletionMasks.Clone(),
			RP = RP,
			PMap = PMap,
			Lucci = Lucci,
			SlotChanger = SlotChanger,
			RiderIntro = RiderIntro,
			Equipment = Equipment?.Clone()
		};
	}
}

internal sealed class LegacyEquipmentRecord
{
	public ushort Character { get; set; }

	public ushort Paint { get; set; }

	public ushort Kart { get; set; }

	public ushort Plate { get; set; }

	public ushort Goggle { get; set; }

	public ushort Balloon { get; set; }

	public ushort Reserved0 { get; set; }

	public ushort HeadBand { get; set; }

	public ushort Reserved1 { get; set; }

	public static LegacyEquipmentRecord FromEquipment(LegacyEquipment equipment)
	{
		ArgumentNullException.ThrowIfNull(equipment);
		return new LegacyEquipmentRecord
		{
			Character = equipment.Character,
			Paint = equipment.Paint,
			Kart = equipment.Kart,
			Plate = equipment.Plate,
			Goggle = equipment.Goggle,
			Balloon = equipment.Balloon,
			Reserved0 = equipment.Reserved0,
			HeadBand = equipment.HeadBand,
			Reserved1 = equipment.Reserved1
		};
	}

	public LegacyEquipment ToEquipment()
	{
		return new LegacyEquipment
		{
			Character = Character,
			Paint = Paint,
			Kart = Kart,
			Plate = Plate,
			Goggle = Goggle,
			Balloon = Balloon,
			Reserved0 = Reserved0,
			HeadBand = HeadBand,
			Reserved1 = Reserved1
		};
	}

	public LegacyEquipmentRecord Clone()
	{
		return FromEquipment(ToEquipment());
	}
}

internal sealed class JsonLegacyProfileStore : ILegacyProfileStore
{
	private const int CurrentVersion = 1;

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	private readonly object _syncRoot = new object();
	private readonly string _path;
	private readonly Dictionary<string, LegacyProfileRecord> _profiles =
		new Dictionary<string, LegacyProfileRecord>(StringComparer.OrdinalIgnoreCase);

	public JsonLegacyProfileStore(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("A profile store path is required.", nameof(path));
		}

		_path = Path.GetFullPath(path);
		lock (_syncRoot)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			LoadLocked();
		}
	}

	public IReadOnlyList<LegacyProfileRecord> LoadAll()
	{
		lock (_syncRoot)
		{
			return _profiles.Values
				.OrderBy(profile => profile.UserNo)
				.Select(profile => profile.Clone())
				.ToArray();
		}
	}

	public void Save(LegacyProfileRecord profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		SaveAll(new[] { profile });
	}

	public void SaveAll(IEnumerable<LegacyProfileRecord> profiles)
	{
		ArgumentNullException.ThrowIfNull(profiles);
		lock (_syncRoot)
		{
			Dictionary<string, LegacyProfileRecord> previousProfiles =
				_profiles.ToDictionary(
					pair => pair.Key,
					pair => pair.Value.Clone(),
					StringComparer.OrdinalIgnoreCase);
			try
			{
				foreach (LegacyProfileRecord profile in profiles)
				{
					Validate(profile);
					_profiles[profile.SourceUsername] = profile.Clone();
				}

				ValidateUniqueUserNumbers(_profiles.Values);
				WriteLocked();
			}
			catch
			{
				_profiles.Clear();
				foreach ((string username, LegacyProfileRecord profile) in previousProfiles)
				{
					_profiles.Add(username, profile);
				}

				throw;
			}
		}
	}

	private void LoadLocked()
	{
		if (!File.Exists(_path))
		{
			WriteLocked();
			return;
		}

		try
		{
			using FileStream stream = new FileStream(
				_path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read);
			LegacyProfileDocument document =
				JsonSerializer.Deserialize<LegacyProfileDocument>(stream, SerializerOptions) ??
				throw new InvalidDataException("The profile document is empty.");
			if (document.Version != CurrentVersion)
			{
				throw new InvalidDataException(
					$"Unsupported profile document version {document.Version}; expected {CurrentVersion}.");
			}

			if (document.Profiles == null)
			{
				throw new InvalidDataException("The profile list is missing.");
			}

			ValidateUniqueUserNumbers(document.Profiles);
			foreach (LegacyProfileRecord profile in document.Profiles)
			{
				Validate(profile);
				if (!_profiles.TryAdd(profile.SourceUsername, profile.Clone()))
				{
					throw new InvalidDataException(
						$"Duplicate source username '{profile.SourceUsername}'.");
				}
			}
		}
		catch (Exception ex) when (ex is JsonException || ex is InvalidDataException)
		{
			string corruptPath = Path.Combine(
				Path.GetDirectoryName(_path)!,
				$"{Path.GetFileName(_path)}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
			File.Move(_path, corruptPath);
			_profiles.Clear();
			LegacyPacketTrace.LogEvent(
				$"[2005 PROFILE] Corrupt profile store moved to '{corruptPath}': {ex.Message}");
			WriteLocked();
		}
	}

	private void WriteLocked()
	{
		string directory = Path.GetDirectoryName(_path)!;
		Directory.CreateDirectory(directory);
		string temporaryPath = Path.Combine(
			directory,
			$".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

		try
		{
			LegacyProfileDocument document = new LegacyProfileDocument
			{
				Version = CurrentVersion,
				Profiles = _profiles.Values
					.OrderBy(profile => profile.UserNo)
					.Select(profile => profile.Clone())
					.ToList()
			};

			using (FileStream stream = new FileStream(
				temporaryPath,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				4096,
				FileOptions.WriteThrough))
			{
				JsonSerializer.Serialize(stream, document, SerializerOptions);
				stream.Flush(flushToDisk: true);
			}

			File.Move(temporaryPath, _path, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	private static void Validate(LegacyProfileRecord profile)
	{
		if (profile == null)
		{
			throw new InvalidDataException("A null profile was found.");
		}

		if (string.IsNullOrWhiteSpace(profile.SourceUsername) ||
			!string.Equals(profile.SourceUsername, profile.SourceUsername.Trim(), StringComparison.Ordinal))
		{
			throw new InvalidDataException("A profile has an invalid source username.");
		}

		if (profile.UserNo == 0)
		{
			throw new InvalidDataException(
				$"Profile '{profile.SourceUsername}' has user number zero.");
		}

		if (string.IsNullOrWhiteSpace(profile.UserId) || string.IsNullOrWhiteSpace(profile.Nickname))
		{
			throw new InvalidDataException(
				$"Profile '{profile.SourceUsername}' has an empty identity or nickname.");
		}

		if (profile.RiderIntro == null || profile.Equipment == null)
		{
			throw new InvalidDataException(
				$"Profile '{profile.SourceUsername}' has missing rider data.");
		}

		if (profile.LicenseLevel > P236LicenseProgress.MaximumLevel)
		{
			throw new InvalidDataException(
				$"Profile '{profile.SourceUsername}' has unsupported license level {profile.LicenseLevel}.");
		}

		if (profile.LicenseCompletionMasks == null ||
			profile.LicenseCompletionMasks.Length != P236LicenseProgress.CompletionMaskCount)
		{
			throw new InvalidDataException(
				$"Profile '{profile.SourceUsername}' must have " +
				$"{P236LicenseProgress.CompletionMaskCount} license completion masks.");
		}
	}

	private static void ValidateUniqueUserNumbers(IEnumerable<LegacyProfileRecord> profiles)
	{
		HashSet<uint> userNumbers = new HashSet<uint>();
		foreach (LegacyProfileRecord profile in profiles)
		{
			if (profile != null && !userNumbers.Add(profile.UserNo))
			{
				throw new InvalidDataException($"Duplicate user number {profile.UserNo}.");
			}
		}
	}

	private sealed class LegacyProfileDocument
	{
		public int Version { get; set; } = CurrentVersion;

		public List<LegacyProfileRecord> Profiles { get; set; } = new List<LegacyProfileRecord>();
	}
}
