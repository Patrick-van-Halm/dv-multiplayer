namespace Multiplayer.Networking.Packets.Common.Train;

public class CommonHoseConnectorDragPacket
{
    public byte PlayerId { get; set; }
    public ushort NetId { get; set; }
    public bool IsFront { get; set; }
    public bool IsMultipleUnit { get; set; }
    public bool Grabbed { get; set; }
}
