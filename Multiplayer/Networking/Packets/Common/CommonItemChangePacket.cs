using LiteNetLib.Utils;
using System.Collections.Generic;
using System;
using System.IO;
using Multiplayer.Networking.Data.Items;

namespace Multiplayer.Networking.Packets.Common;

public class CommonItemChangePacket : INetSerializable
{
    private const int COMPRESS_AFTER_COUNT = 50;

    public List<ItemUpdateData> Items = new List<ItemUpdateData>();

    internal static IEnumerable<CommonItemChangePacket> CreateBatches(
        IReadOnlyList<ItemUpdateData> items)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        foreach (ItemUpdateData item in items)
        {
            if (item == null)
            {
                throw new InvalidDataException(
                    "Item snapshot collection contains a null entry.");
            }

            item.ValidateForSerialization();
        }

        for (int offset = 0; offset < items.Count;)
        {
            int low = 1;
            int high = Math.Min(
                ItemPacketLimits.MaxSnapshots,
                items.Count - offset);
            List<ItemUpdateData> largestBatch = null;
            while (low <= high)
            {
                int count = low + (high - low) / 2;
                List<ItemUpdateData> candidate =
                    CopyRange(items, offset, count);
                if (CanSerialize(candidate))
                {
                    largestBatch = candidate;
                    low = count + 1;
                }
                else
                {
                    high = count - 1;
                }
            }

            if (largestBatch == null)
            {
                throw new InvalidDataException(
                    "An item snapshot exceeds the packet byte limits.");
            }

            yield return new CommonItemChangePacket
            {
                Items = largestBatch,
            };
            offset += largestBatch.Count;
        }
    }

    private static List<ItemUpdateData> CopyRange(
        IReadOnlyList<ItemUpdateData> items,
        int offset,
        int count)
    {
        var result = new List<ItemUpdateData>(count);
        for (int index = 0; index < count; index++)
            result.Add(items[offset + index]);
        return result;
    }

    private static bool CanSerialize(List<ItemUpdateData> items)
    {
        try
        {
            var writer = new NetDataWriter();
            new CommonItemChangePacket
            {
                Items = items,
            }.Serialize(writer);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        //Multiplayer.LogDebug(()=>"CommonItemChangePacket.Deserialize()");
        //Multiplayer.LogDebug(() => $"CommonItemChangePacket.Deserialize()\r\nBytes: {BitConverter.ToString(reader.RawData).Replace("-", " ")}");

        try
        {
            DeserializePayload(reader);

            //Multiplayer.LogDebug(() => $"CommonItemChangePacket.Deserialize() post-itemCount {Items?.Count} ");
        }
        catch (Exception ex)
        {
            Items.Clear();
            Multiplayer.LogError($"Error in CommonItemChangePacket.Deserialize: {ex.Message}");
        }
    }

    internal void DeserializePayload(NetDataReader reader)
    {
        Items.Clear();

        if (reader.GetBool())
            DeserializeCompressed(reader);
        else
            DeserializeRaw(reader);
    }

    private void DeserializeCompressed(NetDataReader reader)
    {
        int itemCount = ReadItemCount(reader);
        int compressedLength = reader.GetInt();
        if (compressedLength < 0 ||
            compressedLength > ItemPacketLimits.MaxCompressedPayloadBytes ||
            compressedLength > reader.AvailableBytes)
        {
            throw new InvalidDataException(
                $"Invalid compressed item payload length: {compressedLength}.");
        }

        var compressedData = new byte[compressedLength];
        reader.GetBytes(compressedData, compressedLength);
        //Multiplayer.LogDebug(() => $"CommonItemChangePacket.DeserializeCompressed() itemCount {itemCount} length: {compressedData.Length}");

        byte[] decompressedData = PacketCompression.Decompress(
            compressedData,
            ItemPacketLimits.MaxDecompressedPayloadBytes);
        //Multiplayer.Log($"CommonItemChangePacket.DeserializeCompressed() Compressed: {compressedData.Length} Decompressed: {decompressedData.Length}");

        NetDataReader decompressedReader = new NetDataReader(decompressedData);
        
        //Items.Capacity = itemCount;

        for (int i = 0; i < itemCount; i++)
        {
            var item = new ItemUpdateData();
            item.Deserialize(decompressedReader);
            Items.Add(item);
        }
    }

    private void DeserializeRaw(NetDataReader reader)
    {
        int itemCount = ReadItemCount(reader);
        if (reader.AvailableBytes >
            ItemPacketLimits.MaxDecompressedPayloadBytes)
        {
            throw new InvalidDataException(
                $"Raw item payload exceeds the packet limit: " +
                $"{reader.AvailableBytes} bytes.");
        }
        //Multiplayer.LogDebug(() => $"CommonItemChangePacket.DeserializeRaw() itemCount: {itemCount}");

        for (int i = 0; i < itemCount; i++)
        {
            var item = new ItemUpdateData();
            item.Deserialize(reader);
            Items.Add(item);
        }
    }

    private static int ReadItemCount(NetDataReader reader)
    {
        int itemCount = reader.GetInt();
        if (itemCount < 0 ||
            itemCount > ItemPacketLimits.MaxSnapshots)
        {
            throw new InvalidDataException(
                $"Invalid item snapshot count: {itemCount}.");
        }

        return itemCount;
    }

    public void Serialize(NetDataWriter writer)
    {
        ValidateOutgoingItems();

        if (Items.Count > COMPRESS_AFTER_COUNT)
            SerializeCompressed(writer);
        else
            SerializeRaw(writer);
    }

    private void ValidateOutgoingItems()
    {
        if (Items == null)
            throw new InvalidDataException("Item snapshot collection cannot be null.");
        if (Items.Count > ItemPacketLimits.MaxSnapshots)
        {
            throw new InvalidDataException(
                $"Item snapshot count exceeds the packet limit: {Items.Count}.");
        }
        if (Items.Exists(item => item == null))
            throw new InvalidDataException("Item snapshot collection contains a null entry.");
        foreach (ItemUpdateData item in Items)
            item.ValidateForSerialization();
    }

    private void SerializeCompressed(NetDataWriter writer)
    {
        //Multiplayer.LogDebug(() => $"CommonItemChangePacket.Serialize() Compressing. Item Count: {Items.Count}");
        NetDataWriter dataWriter = new NetDataWriter();

        foreach (var item in Items)
            item.Serialize(dataWriter);

        if (dataWriter.Length > ItemPacketLimits.MaxDecompressedPayloadBytes)
        {
            throw new InvalidDataException(
                $"Item payload exceeds the decompressed packet limit: {dataWriter.Length} bytes.");
        }

        byte[] compressedData =
            PacketCompression.Compress(dataWriter.CopyData());
        if (compressedData.Length > ItemPacketLimits.MaxCompressedPayloadBytes)
        {
            throw new InvalidDataException(
                $"Item payload exceeds the compressed packet limit: {compressedData.Length} bytes.");
        }

        writer.Put(true); // compressed data stream
        writer.Put(Items.Count);
        // PutBytesWithLength uses a ushort prefix. Item batches can be larger
        // than 64 KiB, so write the bounded payload length as an int to match
        // DeserializeCompressed and avoid truncating the stream.
        writer.Put(compressedData.Length);
        writer.Put(compressedData);
    }

    private void SerializeRaw(NetDataWriter writer)
    {
        //Multiplayer.LogDebug(() => $"CommonItemChangePacket.Serialize() Raw. Item Count: {Items.Count}");
        var payloadWriter = new NetDataWriter();
        foreach (ItemUpdateData item in Items)
            item.Serialize(payloadWriter);
        if (payloadWriter.Length >
            ItemPacketLimits.MaxDecompressedPayloadBytes)
        {
            throw new InvalidDataException(
                $"Raw item payload exceeds the packet limit: " +
                $"{payloadWriter.Length} bytes.");
        }

        writer.Put(false); // uncompressed data stream
        writer.Put(Items.Count);
        writer.Put(payloadWriter.CopyData());
    }
}
