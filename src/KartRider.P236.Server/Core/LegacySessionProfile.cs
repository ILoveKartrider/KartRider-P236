#nullable disable
using System;
using KartRider.P236.Server;

namespace KartRider;

internal sealed class LegacyEquipment
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

	public LegacyEquipment Clone()
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

	public static LegacyEquipment CreateFromStaticTemplate()
	{
		P236ServerOptions options = ServerRuntime.Options;
		return new LegacyEquipment
		{
			Character = options.DefaultCharacter,
			Paint = options.DefaultPaint,
			Kart = options.DefaultKart,
			Plate = options.DefaultPlate,
			Goggle = options.DefaultGoggle,
			Balloon = options.DefaultBalloon,
			Reserved0 = 0,
			HeadBand = options.DefaultHeadBand,
			Reserved1 = 0
		};
	}
}

internal sealed class LegacySessionProfile
{
	private byte _licenseLevel;
	private ushort[] _licenseCompletionMasks;

	public uint UserNo { get; set; }

	public string UserId { get; set; }

	public string Nickname { get; set; }

	public string RiderIntro { get; set; }

	public int RP { get; set; }

	public uint PMap { get; set; }

	public uint Lucci { get; set; }

	public short SlotChanger { get; set; }

	public byte LicenseLevel
	{
		get => _licenseLevel;
		set
		{
			if (value > P236LicenseProgress.MaximumLevel)
			{
				throw new ArgumentOutOfRangeException(
					nameof(LicenseLevel),
					$"P236 license levels must be between 0 and {P236LicenseProgress.MaximumLevel} (L1).");
			}

			_licenseLevel = value;
		}
	}

	public LegacyEquipment Equipment { get; }

	internal string SourceUsername { get; set; }

	private LegacySessionProfile(
		uint userNo,
		string userId,
		string nickname,
		string riderIntro,
		int rp,
		uint pMap,
		uint lucci,
		short slotChanger,
		byte licenseLevel,
		ushort[] licenseCompletionMasks,
		LegacyEquipment equipment)
	{
		UserNo = userNo;
		UserId = userId ?? string.Empty;
		Nickname = nickname ?? string.Empty;
		RiderIntro = riderIntro ?? string.Empty;
		RP = rp;
		PMap = pMap;
		Lucci = lucci;
		SlotChanger = slotChanger;
		LicenseLevel = licenseLevel;
		SetLicenseCompletionMasks(licenseCompletionMasks);
		Equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
	}

	public ushort[] GetLicenseCompletionMasks()
	{
		return (ushort[])_licenseCompletionMasks.Clone();
	}

	public void SetLicenseCompletionMasks(ushort[] masks)
	{
		ArgumentNullException.ThrowIfNull(masks);
		if (masks.Length != P236LicenseProgress.CompletionMaskCount)
		{
			throw new ArgumentException(
				$"Exactly {P236LicenseProgress.CompletionMaskCount} license completion masks are required.",
				nameof(masks));
		}

		_licenseCompletionMasks = (ushort[])masks.Clone();
	}

	public static LegacySessionProfile CreateFromStaticTemplate(uint userNo)
	{
		P236ServerOptions options = ServerRuntime.Options;
		return new LegacySessionProfile(
			userNo,
			options.DefaultUserId,
			options.DefaultNickname,
			string.Empty,
			options.DefaultRp,
			options.DefaultPMap,
			options.DefaultLucci,
			options.DefaultSlotChanger,
			options.DefaultLicenseLevel,
			(ushort[])options.DefaultLicenseCompletionMasks.Clone(),
			LegacyEquipment.CreateFromStaticTemplate());
	}

	public static LegacySessionProfile CreatePersisted(
		string sourceUsername,
		uint userNo,
		string userId,
		string nickname,
		string riderIntro,
		int rp,
		uint pMap,
		uint lucci,
		short slotChanger,
		byte licenseLevel,
		ushort[] licenseCompletionMasks,
		LegacyEquipment equipment)
	{
		LegacySessionProfile profile = new LegacySessionProfile(
			userNo,
			userId,
			nickname,
			riderIntro,
			rp,
			pMap,
			lucci,
			slotChanger,
			licenseLevel,
			licenseCompletionMasks,
			equipment);
		profile.SourceUsername = sourceUsername;
		return profile;
	}
}
