using KartRider.Common.Security;
using KartRider.IO;

namespace KartRider;

internal static class ProtocolResponses
{
    private const uint HandshakeValueA = 2_919_676_295u;
    private const uint HandshakeValueB = 263_300_380u;

    public static void SendFirstMessage(SessionGroup session)
    {
        using OutPacket packet = new("PcFirstMessage");
        packet.WriteUShort(1002);
        packet.WriteUShort(1);
        packet.WriteUShort(236);
        packet.WriteString("http://127.0.0.1/");
        packet.WriteUInt(HandshakeValueA);
        packet.WriteUInt(HandshakeValueB);
        packet.WriteByte(118);
        packet.WriteBytes(new byte[31]);
        session.Client.Send(packet);
        uint iv = HandshakeValueA ^ HandshakeValueB;
        session.Client.RIV = iv;
        session.Client.SIV = iv;
    }

    public static void SendLogin(SessionGroup session)
    {
        using OutPacket packet = new("PrLogin");
        packet.WriteInt();
        packet.WriteUInt(0x0000972A);
        packet.WriteUInt(session.Profile.UserNo);
        packet.WriteString(session.Profile.UserId);
        packet.WriteByte(byte.MaxValue);
        packet.WriteBytes(0, 0, 0);
        packet.WriteInt();
        packet.WriteByte();
        uint pmap = session.Profile.PMap;
        if (LegacyObserverPolicy.IsObserver(session.Profile)) pmap |= 0x40;
        packet.WriteUInt(pmap);
        packet.WriteShort(-1);
        packet.WriteShort();
        packet.WriteByte();
        packet.WriteInt();
        packet.WriteInt();
        packet.WriteInt();
        packet.WriteByte();
        packet.WriteByte(1);
        packet.WriteString("content");
        packet.WriteInt();
        packet.WriteInt(1);
        packet.WriteString("cc");
        packet.WriteString("kr");
        packet.WriteInt(1);
        packet.WriteString("content");
        packet.WriteInt();
        packet.WriteInt(3);
        packet.WriteString("name");
        packet.WriteString("multiplay");
        packet.WriteString("enable");
        packet.WriteString("true");
        packet.WriteString("visible");
        packet.WriteString("true");
        packet.WriteInt();
        session.Client.Send(packet);
    }

    public static void SendRider(SessionGroup session)
    {
        LegacySessionProfile profile = session.Profile;
        LegacyEquipment equipment = profile.Equipment;
        using OutPacket packet = new("LoRpGetRiderPacket");
        packet.WriteByte(1);
        packet.WriteString(profile.Nickname);
        packet.WriteInt();
        packet.WriteShort();
        packet.WriteShort();
        packet.WriteByte(profile.LicenseLevel);
        foreach (ushort mask in profile.GetLicenseCompletionMasks()) packet.WriteUShort(mask);
        packet.WriteShort(); packet.WriteShort(); packet.WriteShort();
        packet.WriteUShort(equipment.Character);
        packet.WriteUShort(equipment.Paint);
        packet.WriteUShort(equipment.Kart);
        packet.WriteUShort(equipment.Plate);
        packet.WriteUShort(equipment.Goggle);
        packet.WriteUShort(equipment.Balloon);
        packet.WriteUShort(equipment.Reserved0);
        packet.WriteUShort(equipment.HeadBand);
        packet.WriteUShort(equipment.Reserved1);
        packet.WriteString(profile.RiderIntro);
        packet.WriteUInt(profile.Lucci);
        packet.WriteInt(profile.RP);
        for (int i = 0; i < 4; i++) packet.WriteInt();
        session.Client.Send(packet);
    }

    public static void SendInventory(SessionGroup session)
    {
        // P236's garage expects normal itemTable kart IDs. Advertising the
        // special practice-kart ID 0 as an owned body crashes the kart list.
        SendInventoryCategory(session, 3, 1, 72, 1);
        SendInventoryCategory(session, 1, 1, 16, 1);
        SendInventoryCategory(session, 11, 1, 6, 30_000);
        SendInventoryCategory(session, 8, 1, 5, 1);
        SendInventoryCategory(session, 9, 1, 25, 30_000);
        SendInventoryCategory(session, 7, 1, 1, session.Profile.SlotChanger);
        SendInventoryCategory(session, 13, 1, 1, 1);
        SendInventoryCategory(session, 6, 1, 2, 1);
        SendInventoryCategory(session, 10, 1, 2, 1);
        SendInventoryCategory(session, 14, 1, 6, 1);
        SendInventoryCategory(session, 4, 1, 43, 1);
        SendInventoryCategory(session, 2, 1, 11, 1);
    }

    private static void SendInventoryCategory(
        SessionGroup session,
        short category,
        short firstItem,
        short count,
        short quantity)
    {
        using OutPacket packet = new("LoRpGetRiderItemPacket");
        packet.WriteInt(count);
        for (int offset = 0; offset < count; offset++)
        {
            short item = checked((short)(firstItem + offset));
            packet.WriteShort(category);
            packet.WriteShort(item);
            packet.WriteShort(quantity);
            packet.WriteByte();
            packet.WriteByte();
            packet.WriteShort(-1);
            packet.WriteShort();
        }
        session.Client.Send(packet);
    }

    public static void SendRiderInfo(SessionGroup session, string requestedNickname)
    {
        using OutPacket packet = new("PrGetRiderInfo");
        if (!string.Equals(requestedNickname, session.Profile.Nickname, StringComparison.Ordinal))
        {
            packet.WriteByte();
            session.Client.Send(packet);
            return;
        }
        packet.WriteByte(1);
        packet.WriteString(session.Profile.Nickname);
        packet.WriteString(session.Profile.Nickname);
        packet.WriteUInt(0x0000972A);
        packet.WriteByte();
        for (int i = 0; i < 9; i++) packet.WriteShort();
        packet.WriteString(string.Empty);
        packet.WriteInt(session.Profile.RP);
        packet.WriteByte(); packet.WriteByte();
        packet.WriteShort(); packet.WriteShort();
        packet.WriteByte(); packet.WriteByte();
        session.Client.Send(packet);
    }

    public static void SendChannelCatalog(SessionGroup session)
    {
        (byte Id, string Name)[] groups =
        [
            (1, "novice"), (2, "rookieIntro"), (3, "rookie"), (4, "lv3"),
            (5, "lv2"), (6, "arena"), (7, "event")
        ];
        using OutPacket catalog = new();
        catalog.WriteInt(groups.Length);
        foreach ((byte id, string name) in groups) { catalog.WriteByte(id); catalog.WriteString(name); }
        catalog.WriteInt(groups.Length);
        for (ushort index = 0; index < groups.Length; index++)
        {
            catalog.WriteUShort((ushort)(index + 1));
            catalog.WriteString($"{groups[index].Name}_4");
            catalog.WriteByte(groups[index].Id);
            catalog.WriteUInt();
        }
        byte[] encoded = KREncodedBlock.Encode(catalog.ToArray(), KREncodedBlock.EncodeFlag.ZLib, null);
        using OutPacket reply = new("ChRequestChStaticReplyPacket");
        reply.WriteBool(true);
        reply.WriteInt(encoded.Length);
        reply.WriteBytes(encoded);
        session.Client.Send(reply);
    }

    public static void SendEmpty(SessionGroup session, string packetName)
    {
        using OutPacket packet = new(packetName);
        session.Client.Send(packet);
    }
}
