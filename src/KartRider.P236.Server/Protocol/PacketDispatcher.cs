using KartRider.Common.Utilities;
using KartRider.IO;

namespace KartRider;

internal static class PacketDispatcher
{
    private static readonly uint PqLogin = Hash("PqLogin");
    private static readonly uint Ping = Hash("LoPingRequestPacket");
    private static readonly uint MachineInfo = Hash("PqReportMachineInfo");
    private static readonly uint Rider = Hash("LoRqGetRiderPacket");
    private static readonly uint Inventory = Hash("LoRqGetRiderItemPacket");
    private static readonly uint RiderInfo = Hash("PqGetRiderInfo");
    private static readonly uint ChannelStatic = Hash("ChRequestChStaticRequestPacket");
    private static readonly uint ChannelDynamic = Hash("ChRequestChDynamicRequestPacket");
    private static readonly uint DynamicCommand = Hash("PqDynamicCommand");
    private static readonly uint StartSingle = Hash("LoRqStartSinglePacket");
    private static readonly uint UpdateCompletion = Hash("LoRqUpdateCbPacket");
    private static readonly uint AddRacingTime = Hash("LoRqAddRacingTimePacket");
    private static readonly uint GameReport = Hash("GameReportPacket");
    private static readonly uint GameSlot = Hash("GameSlotPacket");
    private static readonly uint GiftIncoming = Hash("SpRqGetGiftListIncomingPacket");
    private static readonly uint RemainCash = Hash("SpRqRemainCashPacket");
    private static readonly uint ServerTime = Hash("PqServerTime");
    private static readonly uint EnterShop = Hash("SpReqEnterShopPacket");
    private static readonly uint CloseRoom = Hash("GrRequestClosePacket");

    public static void Handle(SessionGroup session, InPacket packet)
    {
        lock (session.m_lock)
        {
            if (packet.Available < 4) throw new PacketReadException("Packet has no RTTI hash.");
            uint hash = packet.ReadUInt();
            LegacyPacketTrace.LogReceive(hash, packet);
            if (LegacyMultiplayerHandlers.TryHandle(session, hash, packet)) return;

            if (hash == PqLogin) { HandleLogin(session, packet); return; }
            if (hash == Ping) { ProtocolResponses.SendEmpty(session, "LoPingReplyPacket"); return; }
            if (hash == MachineInfo) { ProtocolResponses.SendEmpty(session, "PrReportMachineInfo"); return; }
            if (hash == Rider) { ProtocolResponses.SendRider(session); return; }
            if (hash == Inventory) { ProtocolResponses.SendInventory(session); return; }
            if (hash == RiderInfo) { ProtocolResponses.SendRiderInfo(session, packet.ReadString()); return; }
            if (hash == ChannelStatic) { ProtocolResponses.SendChannelCatalog(session); return; }
            if (hash == ChannelDynamic) { ProtocolResponses.SendEmpty(session, "ChRequestChDynamicReplyPacket"); return; }
            if (hash == DynamicCommand) { SendDynamicCommand(session); return; }
            if (hash == StartSingle) { LegacyPacketHandlers.HandleStartSingle(session, packet); return; }
            if (hash == UpdateCompletion) { LegacyPacketHandlers.HandleUpdateCompletion(session, packet); return; }
            if (hash == AddRacingTime) { LegacyPacketHandlers.HandleAddRacingTime(session, packet); return; }
            if (hash == GameReport) { LegacyPacketHandlers.HandleGameReport(session, packet); return; }
            if (hash == GameSlot) { LegacyPacketHandlers.HandleGameSlot(session, packet); return; }
            if (hash == GiftIncoming) { SendGiftIncoming(session); return; }
            if (hash == RemainCash) { SendRemainCash(session); return; }
            if (hash == ServerTime) { SendServerTime(session); return; }
            if (hash == EnterShop || hash == CloseRoom) return;
            LegacyPacketTrace.LogEvent($"[P236] Unhandled TCP packet 0x{hash:X8}, body={packet.Available}.");
        }
    }

    private static void HandleLogin(SessionGroup session, InPacket packet)
    {
        string username;
        try { username = LegacyLoginProfileReader.ReadUsername(packet); }
        catch (Exception exception) when (exception is FormatException or PacketReadException)
        {
            username = $"rider-{Guid.NewGuid():N}";
            LegacyPacketTrace.LogEvent($"[P236 LOGIN] Invalid profile, assigned temporary identity: {exception.Message}");
        }
        RouterListener.AssignLoginUsername(session, username);
        ProtocolResponses.SendLogin(session);
    }

    private static void SendDynamicCommand(SessionGroup session)
    {
        using OutPacket packet = new("PrDynamicCommand");
        packet.WriteByte();
        packet.WriteInt();
        session.Client.Send(packet);
    }

    private static void SendGiftIncoming(SessionGroup session)
    {
        using OutPacket packet = new("SpRpGetGiftListIncomingPacket");
        packet.WriteInt(); packet.WriteInt(); packet.WriteInt();
        session.Client.Send(packet);
    }

    private static void SendRemainCash(SessionGroup session)
    {
        using OutPacket packet = new("SpRpRemainCashPacket");
        packet.WriteUInt(); packet.WriteUInt();
        session.Client.Send(packet);
    }

    private static void SendServerTime(SessionGroup session)
    {
        using OutPacket packet = new("PrServerTime");
        packet.WriteUInt(0x0000972A);
        session.Client.Send(packet);
    }

    private static uint Hash(string name) => Adler32Helper.GenerateAdler32_ASCII(name);
}
