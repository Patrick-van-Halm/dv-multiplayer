using MPAPI.Interfaces.Packets;
using System.IO;

namespace Multiplayer.Networking.Data.Customization;

public enum RoadrunnerAction : byte
{
    Start,
    Acknowledge,
}

public sealed class RoadrunnerActionPacket : ISerializablePacket
{
    public ushort GadgetItemNetId;
    public RoadrunnerAction Action;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(GadgetItemNetId);
        writer.Write((byte)Action);
    }

    public void Deserialize(BinaryReader reader)
    {
        GadgetItemNetId = reader.ReadUInt16();
        Action = (RoadrunnerAction)reader.ReadByte();
    }
}
