namespace KartRider.IO;

internal sealed class PacketReadException : IOException
{
    public PacketReadException(string message) : base(message) { }
}
