namespace Multiplayer.Networking.Packets.Serverbound;

public sealed class ServerboundLocomotiveRemotePairPacket
{
    public ushort RemoteItemNetId { get; set; }
    public ushort LocomotiveNetId { get; set; }
}
