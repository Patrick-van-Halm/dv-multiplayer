using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Items;
using UnityEngine;
using Xunit;

namespace Multiplayer.Tests;

public class ItemUpdateDataTests
{
    [Fact]
    public void FullSync_RoundTripsContainerOwnershipAndTrackedState()
    {
        var expected = new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.FullSync,
            ItemNetId = 17,
            ItemState = ItemState.InContainer,
            ItemPosition = new Vector3(1f, 2f, 3f),
            ItemRotation = new Quaternion(0f, 0.5f, 0f, 0.5f),
            Player = 4,
            ContainerNetId = 29,
            ContainerSlot = 2,
            States = new Dictionary<string, object>
            {
                ["item.enabled"] = true,
                ["item.count"] = 12,
                ["item.label"] = "cargo",
            },
        };

        ItemUpdateData result = RoundTrip(expected);

        Assert.Equal(expected.UpdateType, result.UpdateType);
        Assert.Equal(17, result.ItemNetId);
        Assert.Equal(ItemState.InContainer, result.ItemState);
        Assert.Equal(4, result.Player);
        Assert.Equal(29, result.ContainerNetId);
        Assert.Equal(2, result.ContainerSlot);
        Assert.Equal(true, result.States["item.enabled"]);
        Assert.Equal(12, result.States["item.count"]);
        Assert.Equal("cargo", result.States["item.label"]);
    }

    [Fact]
    public void Create_RoundTripsAuthoritativeCreationCorrelation()
    {
        var expected = new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.Create,
            ItemNetId = 31,
            CreationRequestId = 1234,
            PrefabName = "test-prefab",
            ItemState = ItemState.InHand,
            ItemPosition = new Vector3(4f, 5f, 6f),
            ItemRotation = Quaternion.identity,
            Player = 7,
        };

        ItemUpdateData result = RoundTrip(expected);

        Assert.Equal(31, result.ItemNetId);
        Assert.Equal((uint)1234, result.CreationRequestId);
        Assert.Equal("test-prefab", result.PrefabName);
        Assert.Equal(ItemState.InHand, result.ItemState);
        Assert.Equal(7, result.Player);
        Assert.Equal(expected.ItemPosition, result.ItemPosition);
        Assert.Equal(expected.ItemRotation, result.ItemRotation);
    }

    [Fact]
    public void InventoryState_RoundTripsQuickAccessLayout()
    {
        var expected = new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.ItemState,
            ItemNetId = 41,
            ItemState = ItemState.InInventory,
            Player = 3,
            InventorySlot = 5,
            InLockedSlot = true,
            IsDropped = false,
        };

        ItemUpdateData result = RoundTrip(expected);

        Assert.Equal(5, result.InventorySlot);
        Assert.True(result.InLockedSlot);
        Assert.False(result.IsDropped);
    }

    [Theory]
    [InlineData(-2, false, false)]
    [InlineData(36, false, false)]
    [InlineData(-1, true, false)]
    [InlineData(-1, false, true)]
    public void InventoryState_RejectsInvalidLayout(
        int slot,
        bool locked,
        bool dropped)
    {
        var snapshot = new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.ItemState,
            ItemNetId = 41,
            ItemState = ItemState.InInventory,
            InventorySlot = slot,
            InLockedSlot = locked,
            IsDropped = dropped,
        };

        Assert.False(snapshot.HasValidInventoryLayout());
        Assert.Throws<InvalidDataException>(
            () => snapshot.Serialize(new NetDataWriter()));
    }

    [Fact]
    public void InventoryState_AllowsTransientUnassignedSlot()
    {
        var snapshot = new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.Create,
            ItemNetId = 41,
            CreationRequestId = 99,
            PrefabName = "test-prefab",
            ItemState = ItemState.InHand,
            ItemRotation = Quaternion.identity,
            InventorySlot = -1,
        };

        Assert.True(snapshot.HasValidInventoryLayout());
        Assert.Equal(-1, RoundTrip(snapshot).InventorySlot);
    }

    [Fact]
    public void Deserialize_RejectsTrackedValueCountAboveLimit()
    {
        var writer = new NetDataWriter();
        writer.Put((byte)ItemUpdateData.ItemUpdateType.ObjectState);
        writer.Put((ushort)17);
        writer.Put((byte)ItemState.InHand);
        writer.Put((byte)4);
        writer.Put(ItemPacketLimits.MaxTrackedValues + 1);

        var result = new ItemUpdateData();

        Assert.Throws<InvalidDataException>(
            () => result.Deserialize(
                new NetDataReader(writer.CopyData())));
    }

    [Fact]
    public void Serialize_RejectsOversizedTrackedStringBeforeWriting()
    {
        var snapshot = new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.ObjectState,
            ItemNetId = 17,
            ItemState = ItemState.InHand,
            States = new Dictionary<string, object>
            {
                ["item.label"] = new string(
                    'x',
                    ItemPacketLimits.MaxTrackedStringLength + 1),
            },
        };
        var writer = new NetDataWriter();

        Assert.Throws<InvalidDataException>(
            () => snapshot.Serialize(writer));
        Assert.Equal(0, writer.Length);
    }

    [Fact]
    public void Serialize_RejectsNonFinitePositionBeforeWriting()
    {
        var snapshot = new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.ItemPosition,
            ItemNetId = 17,
            ItemState = ItemState.InHand,
            ItemPosition = new Vector3(float.NaN, 0f, 0f),
            ItemRotation = Quaternion.identity,
        };
        var writer = new NetDataWriter();

        Assert.Throws<InvalidDataException>(
            () => snapshot.Serialize(writer));
        Assert.Equal(0, writer.Length);
    }

    [Theory]
    [InlineData(ItemUpdateData.ItemUpdateType.None)]
    [InlineData(
        ItemUpdateData.ItemUpdateType.Destroy |
        ItemUpdateData.ItemUpdateType.ObjectState)]
    [InlineData(
        ItemUpdateData.ItemUpdateType.Create |
        ItemUpdateData.ItemUpdateType.ItemPosition)]
    [InlineData((ItemUpdateData.ItemUpdateType)128)]
    public void Serialize_RejectsInvalidUpdateTypeBeforeWriting(
        ItemUpdateData.ItemUpdateType updateType)
    {
        var snapshot = new ItemUpdateData
        {
            UpdateType = updateType,
            ItemNetId = 17,
        };
        var writer = new NetDataWriter();

        Assert.Throws<InvalidDataException>(
            () => snapshot.Serialize(writer));
        Assert.Equal(0, writer.Length);
    }

    [Fact]
    public void Serialize_RejectsTrackedValueCountBeforeWriting()
    {
        var snapshot = new ItemUpdateData
        {
            UpdateType = ItemUpdateData.ItemUpdateType.ObjectState,
            ItemNetId = 17,
            ItemState = ItemState.InHand,
            States = Enumerable.Range(
                    0,
                    ItemPacketLimits.MaxTrackedValues + 1)
                .ToDictionary(
                    index => $"item.{index}",
                    index => (object)index),
        };
        var writer = new NetDataWriter();

        Assert.Throws<InvalidDataException>(
            () => snapshot.Serialize(writer));
        Assert.Equal(0, writer.Length);
    }

    private static ItemUpdateData RoundTrip(ItemUpdateData expected)
    {
        var writer = new NetDataWriter();
        expected.Serialize(writer);

        var result = new ItemUpdateData();
        result.Deserialize(new NetDataReader(writer.CopyData()));
        return result;
    }
}
