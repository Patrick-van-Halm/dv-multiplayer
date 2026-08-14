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
