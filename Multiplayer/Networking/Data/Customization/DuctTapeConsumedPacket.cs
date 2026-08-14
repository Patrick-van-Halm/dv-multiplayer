using MPAPI.Interfaces.Packets;
using System.IO;

namespace Multiplayer.Networking.Data.Customization;

public sealed class DuctTapeConsumedPacket : ISerializablePacket
{
    public ushort TapeItemNetId;
    public ushort EmptyTapeItemNetId;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(TapeItemNetId);
        writer.Write(EmptyTapeItemNetId);
    }

    public void Deserialize(BinaryReader reader)
    {
        TapeItemNetId = reader.ReadUInt16();
        EmptyTapeItemNetId = reader.ReadUInt16();
    }
}

public enum RoadrunnerSyncAction : byte
{
    Start,
    Acknowledge,
}

public sealed class RoadrunnerSyncPacket : ISerializablePacket
{
    public ushort GadgetItemNetId;
    public RoadrunnerSyncAction Action;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(GadgetItemNetId);
        writer.Write((byte)Action);
    }

    public void Deserialize(BinaryReader reader)
    {
        GadgetItemNetId = reader.ReadUInt16();
        Action = (RoadrunnerSyncAction)reader.ReadByte();
    }
}
