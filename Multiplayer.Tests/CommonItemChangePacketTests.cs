using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Packets.Common;
using Xunit;

namespace Multiplayer.Tests;

public class CommonItemChangePacketTests
{
    [Fact]
    public void Deserialize_RejectsSnapshotCountAboveLimit()
    {
        var writer = new NetDataWriter();
        writer.Put(false);
        writer.Put(ItemPacketLimits.MaxSnapshots + 1);
        var packet = new CommonItemChangePacket();

        Assert.Throws<InvalidDataException>(
            () => packet.DeserializePayload(
                new NetDataReader(writer.CopyData())));

        Assert.Empty(packet.Items);
    }

    [Fact]
    public void Deserialize_RejectsCompressedLengthBeforeReadingPayload()
    {
        var writer = new NetDataWriter();
        writer.Put(true);
        writer.Put(1);
        writer.Put(ItemPacketLimits.MaxCompressedPayloadBytes + 1);
        var packet = new CommonItemChangePacket();

        Assert.Throws<InvalidDataException>(
            () => packet.DeserializePayload(
                new NetDataReader(writer.CopyData())));

        Assert.Empty(packet.Items);
    }

    [Fact]
    public void Decompress_RejectsOutputAboveLimit()
    {
        byte[] compressed = PacketCompression.Compress(new byte[1024]);

        Assert.Throws<InvalidDataException>(
            () => PacketCompression.Decompress(compressed, 128));
    }

    [Fact]
    public void CreateBatches_SplitsAtSnapshotLimitWithoutLosingOrder()
    {
        var items = Enumerable.Range(0, ItemPacketLimits.MaxSnapshots + 1)
            .Select(index => new ItemUpdateData
            {
                UpdateType = ItemUpdateData.ItemUpdateType.Destroy,
                ItemNetId = (ushort)index,
            })
            .ToList();

        var batches = CommonItemChangePacket.CreateBatches(items).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal(ItemPacketLimits.MaxSnapshots, batches[0].Items.Count);
        Assert.Single(batches[1].Items);
        Assert.Equal(items.Select(item => item.ItemNetId),
            batches.SelectMany(packet => packet.Items).Select(item => item.ItemNetId));
    }

    [Fact]
    public void Serialize_RejectsSnapshotCountAboveLimit()
    {
        var packet = new CommonItemChangePacket
        {
            Items = Enumerable.Range(0, ItemPacketLimits.MaxSnapshots + 1)
                .Select(index => new ItemUpdateData { ItemNetId = (ushort)index })
                .ToList()
        };

        Assert.Throws<InvalidDataException>(
            () => packet.Serialize(new NetDataWriter()));
    }

    [Fact]
    public void CreateBatches_SplitsBeforeDecompressedByteLimit()
    {
        string value = new string(
            'x',
            ItemPacketLimits.MaxTrackedStringLength);
        var states = Enumerable.Range(
                0,
                ItemPacketLimits.MaxTrackedValues)
            .ToDictionary(
                index => $"state.{index}",
                _ => (object)value);
        var items = Enumerable.Range(0, 40)
            .Select(index => new ItemUpdateData
            {
                UpdateType =
                    ItemUpdateData.ItemUpdateType.ObjectState,
                ItemNetId = (ushort)(index + 1),
                ItemState = ItemState.InHand,
                States = new Dictionary<string, object>(states),
            })
            .ToList();

        List<CommonItemChangePacket> batches =
            CommonItemChangePacket.CreateBatches(items).ToList();

        Assert.True(batches.Count > 1);
        Assert.Equal(
            items.Select(item => item.ItemNetId),
            batches.SelectMany(packet => packet.Items)
                .Select(item => item.ItemNetId));
        foreach (CommonItemChangePacket batch in batches)
            batch.Serialize(new NetDataWriter());
    }

    [Fact]
    public void CompressedPayload_RoundTripsWhenLargerThanUShort()
    {
        var random = new Random(1234);
        var items = new List<ItemUpdateData>();
        for (int index = 0; index < 24; index++)
        {
            byte[] bytes = new byte[4096];
            random.NextBytes(bytes);
            string value = Convert.ToBase64String(bytes)
                .Substring(0, ItemPacketLimits.MaxTrackedStringLength);
            items.Add(new ItemUpdateData
            {
                UpdateType = ItemUpdateData.ItemUpdateType.ObjectState,
                ItemNetId = (ushort)(index + 1),
                ItemState = ItemState.Dropped,
                States = new Dictionary<string, object>
                {
                    ["state"] = value,
                },
            });
        }

        var packet = new CommonItemChangePacket { Items = items };
        var writer = new NetDataWriter();
        packet.Serialize(writer);

        Assert.True(writer.Length > ushort.MaxValue);

        var decoded = new CommonItemChangePacket();
        decoded.DeserializePayload(new NetDataReader(writer.CopyData()));

        Assert.Equal(items.Count, decoded.Items.Count);
        Assert.Equal(
            items.Select(item => item.ItemNetId),
            decoded.Items.Select(item => item.ItemNetId));
    }
}
