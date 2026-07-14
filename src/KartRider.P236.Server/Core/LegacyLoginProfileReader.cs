#nullable disable
using System;
using KartRider.Common.Utilities;
using KartRider.IO;

namespace KartRider;

internal static class LegacyLoginProfileReader
{
	private const int MaximumDepth = 8;
	private const int MaximumNodes = 64;
	private const int MaximumAttributesPerNode = 64;
	private const int MaximumChildrenPerNode = 64;
	private const int MaximumStringLength = 1024;

	private static readonly uint AccountDataProfileHash =
		Adler32Helper.GenerateAdler32_ASCII("AccountDataProfile");

	public static string ReadUsername(InPacket packet)
	{
		ArgumentNullException.ThrowIfNull(packet);
		if (packet.Available < sizeof(uint))
		{
			throw new FormatException("PqLogin does not contain an AccountDataProfile packet.");
		}

		uint nestedPacketHash = packet.ReadUInt();
		if (nestedPacketHash != AccountDataProfileHash)
		{
			throw new FormatException(
				$"PqLogin nested packet 0x{nestedPacketHash:X8} is not AccountDataProfile.");
		}

		int nodeCount = 0;
		string username = null;
		string rootName = ReadNode(packet, 0, ref nodeCount, ref username);
		if (!string.Equals(rootName, "profile", StringComparison.OrdinalIgnoreCase))
		{
			throw new FormatException($"AccountDataProfile root is '{rootName}', not 'profile'.");
		}

		if (string.IsNullOrWhiteSpace(username))
		{
			throw new FormatException("AccountDataProfile does not contain a username value.");
		}

		return username.Trim();
	}

	private static string ReadNode(
		InPacket packet,
		int depth,
		ref int nodeCount,
		ref string username)
	{
		if (depth > MaximumDepth)
		{
			throw new FormatException($"BML nesting exceeds {MaximumDepth} levels.");
		}

		nodeCount++;
		if (nodeCount > MaximumNodes)
		{
			throw new FormatException($"BML contains more than {MaximumNodes} nodes.");
		}

		string name = ReadLimitedString(packet, "node name");
		string value = ReadLimitedString(packet, $"value for '{name}'");

		int attributeCount = ReadCount(packet, MaximumAttributesPerNode, "attribute");
		for (int index = 0; index < attributeCount; index++)
		{
			ReadLimitedString(packet, $"attribute {index} name");
			ReadLimitedString(packet, $"attribute {index} value");
		}

		if (username == null && string.Equals(name, "username", StringComparison.OrdinalIgnoreCase))
		{
			username = value;
		}

		int childCount = ReadCount(packet, MaximumChildrenPerNode, "child");
		for (int index = 0; index < childCount; index++)
		{
			ReadNode(packet, depth + 1, ref nodeCount, ref username);
		}

		return name;
	}

	private static int ReadCount(InPacket packet, int maximum, string kind)
	{
		int count = packet.ReadInt();
		if (count < 0 || count > maximum)
		{
			throw new FormatException($"BML {kind} count {count} is outside 0..{maximum}.");
		}

		return count;
	}

	private static string ReadLimitedString(InPacket packet, string field)
	{
		string value = packet.ReadString();
		if (value.Length > MaximumStringLength)
		{
			throw new FormatException(
				$"BML {field} length {value.Length} exceeds {MaximumStringLength} characters.");
		}

		return value;
	}
}
