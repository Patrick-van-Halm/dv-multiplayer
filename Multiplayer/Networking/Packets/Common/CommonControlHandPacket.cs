using UnityEngine;

namespace Multiplayer.Networking.Packets.Common;

public enum ControlHandInteraction : byte
{
    Grab,
    Ungrab,
    Use,
}

public class CommonControlHandPacket
{
    public byte PlayerId { get; set; }
    public ControlHandInteraction Interaction { get; set; }
    public Vector3 TargetPosition { get; set; }
}
