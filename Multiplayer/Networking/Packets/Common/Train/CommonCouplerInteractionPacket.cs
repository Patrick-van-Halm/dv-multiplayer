using System;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common.Train;


public class CommonCouplerInteractionPacket
{
    public byte PlayerId { get; set; }
    public ushort NetId { get; set; }
    public ushort OtherNetId { get; set; }
    public ushort RemoteItemNetId { get; set; }
    public bool IsFrontCoupler { get; set; }
    public bool IsFrontOtherCoupler { get; set; }
    public ushort Flags { get; set; }
    public Vector3 HandTargetLocalPosition { get; set; }
    public Quaternion HandTargetLocalRotation { get; set; }
}
