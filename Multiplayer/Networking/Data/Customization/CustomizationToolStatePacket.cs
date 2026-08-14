using MPAPI.Interfaces.Packets;
using Multiplayer.Networking.Data.Items;
using System;
using System.Collections.Generic;
using System.IO;

namespace Multiplayer.Networking.Data.Customization;

public sealed class ClientboundCustomizationToolStatePacket : ISerializablePacket
{
    public List<ItemUpdateData> Items { get; set; } = new();

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Items.Count);
        foreach (ItemUpdateData item in Items)
            CustomizationPacketIO.WriteItemUpdate(writer, item);
    }

    public void Deserialize(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > 10000)
            throw new InvalidDataException($"Invalid customization tool item count {count}");

        Items = new List<ItemUpdateData>(count);
        for (int i = 0; i < count; i++)
            Items.Add(CustomizationPacketIO.ReadItemUpdate(reader));
    }
}
