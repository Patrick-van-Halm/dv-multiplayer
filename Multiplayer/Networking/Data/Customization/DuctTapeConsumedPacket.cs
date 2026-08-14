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
    Snapshot,
}

public sealed class RoadrunnerSyncPacket : ISerializablePacket
{
    public ushort GadgetItemNetId;
    public RoadrunnerSyncAction Action;
    public int LengthMeters;
    public float Countup;
    public bool HasCompleted;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(GadgetItemNetId);
        writer.Write((byte)Action);
        writer.Write(LengthMeters);
        writer.Write(Countup);
        writer.Write(HasCompleted);
    }

    public void Deserialize(BinaryReader reader)
    {
        GadgetItemNetId = reader.ReadUInt16();
        Action = (RoadrunnerSyncAction)reader.ReadByte();
        LengthMeters = reader.ReadInt32();
        Countup = reader.ReadSingle();
        HasCompleted = reader.ReadBoolean();
    }
}
