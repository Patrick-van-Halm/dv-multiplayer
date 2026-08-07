using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Serialization;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Multiplayer.Networking.Data.Items;

public class ItemUpdateData
{
    [Flags]
    public enum ItemUpdateType : byte
    {
        None = 0,
        Create = 1,
        Destroy = 2,
        ItemState = 4,
        ItemPosition = 8,
        ObjectState = 16,
        FullSync = ItemState | ItemPosition | ObjectState,
    }

    public ItemUpdateType UpdateType { get; set; }
    public ushort ItemNetId { get; set; }
    public uint CreationRequestId { get; set; }
    public string PrefabName { get; set; }
    public ItemState ItemState { get; set; }
    public Vector3 ItemPosition { get; set; }
    public Quaternion ItemRotation { get; set; }
    public Vector3 ThrowDirection { get; set; }
    public byte Player { get; set; }
    public ushort CarNetId { get; set; }
    public bool AttachedFront  { get; set; }
    public ushort ContainerNetId { get; set; }
    public int ContainerSlot { get; set; } = -1;
<<<<<<< HEAD
    public int InventorySlot { get; set; } = -1;
    public bool InLockedSlot { get; set; }
    public bool IsDropped { get; set; }
=======
>>>>>>> 64b64ddac4c01653a5f35094f35d4762e08207d6
    public Dictionary<string, object> States { get; set; }

    public void Serialize(NetDataWriter writer)
    {
        ValidateForSerialization();

        writer.Put((byte)UpdateType);
        writer.Put(ItemNetId);

        if (UpdateType == ItemUpdateType.Destroy)
            return;

        writer.Put((byte)ItemState);

        if (UpdateType.HasFlag(ItemUpdateType.Create))
        {
            writer.Put(PrefabName);
            writer.Put(CreationRequestId);
        }

        // Last-holder ownership applies to every item state and must also be
        // available when a dropped item is first created for a late joiner.
        writer.Put(Player);

        bool hasStatePayload =
            UpdateType.HasFlag(ItemUpdateType.Create) ||
            UpdateType.HasFlag(ItemUpdateType.ItemState);
        bool hasPositionPayload =
            UpdateType.HasFlag(ItemUpdateType.Create) ||
            UpdateType.HasFlag(ItemUpdateType.ItemPosition) ||
            (hasStatePayload &&
                (ItemState == ItemState.Dropped ||
                 ItemState == ItemState.Thrown));

        if (hasPositionPayload)
        {
            Vector3Serializer.Serialize(writer, ItemPosition);
            QuaternionSerializer.Serialize(writer, ItemRotation);
        }

        if (hasStatePayload)
        {
            if (ItemState == ItemState.Thrown)
            {
                Vector3Serializer.Serialize(writer, ThrowDirection);
            }
            else if (ItemState == ItemState.Attached)
            {
                writer.Put(CarNetId);
                writer.Put(AttachedFront);
            }
            else if (ItemState == ItemState.InContainer)
            {
                writer.Put(ContainerNetId);
                writer.Put(ContainerSlot);
            }
<<<<<<< HEAD

            if (ItemState == ItemState.InHand ||
                ItemState == ItemState.InInventory)
            {
                writer.Put(InventorySlot);
                writer.Put(InLockedSlot);
                writer.Put(IsDropped);
            }
=======
>>>>>>> 64b64ddac4c01653a5f35094f35d4762e08207d6
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            if (States == null)
                writer.Put(0);
            else
            {
                writer.Put(States.Count);
                foreach (var state in States)
                {
                    writer.Put(state.Key);
                    SerializeTrackedValue(writer, state.Value);
                }
            }
        }
    }

    internal void ValidateForSerialization()
    {
        const ItemUpdateType knownTypes =
            ItemUpdateType.Create |
            ItemUpdateType.Destroy |
            ItemUpdateType.ItemState |
            ItemUpdateType.ItemPosition |
            ItemUpdateType.ObjectState;

        if (UpdateType == ItemUpdateType.None ||
            (UpdateType & ~knownTypes) != 0 ||
            UpdateType.HasFlag(ItemUpdateType.Destroy) &&
            UpdateType != ItemUpdateType.Destroy ||
            UpdateType.HasFlag(ItemUpdateType.Create) &&
            UpdateType != ItemUpdateType.Create)
        {
            throw new InvalidDataException(
                $"Invalid item update type: {UpdateType}.");
        }

        if (UpdateType == ItemUpdateType.Destroy)
            return;

        if (!Enum.IsDefined(typeof(ItemState), ItemState))
        {
            throw new InvalidDataException(
                $"Invalid item state: {ItemState}.");
        }

        if (UpdateType == ItemUpdateType.Create &&
            (string.IsNullOrEmpty(PrefabName) ||
             PrefabName.Length >
                 ItemPacketLimits.MaxPrefabNameLength))
        {
            throw new InvalidDataException(
                "Create snapshots require a bounded prefab name.");
        }

        bool hasStatePayload =
            UpdateType.HasFlag(ItemUpdateType.Create) ||
            UpdateType.HasFlag(ItemUpdateType.ItemState);
        bool hasPositionPayload =
            UpdateType.HasFlag(ItemUpdateType.Create) ||
            UpdateType.HasFlag(ItemUpdateType.ItemPosition) ||
            hasStatePayload &&
            (ItemState == ItemState.Dropped ||
             ItemState == ItemState.Thrown);

        if (hasPositionPayload &&
            (!ItemPosition.IsFinite() ||
             !ItemRotation.IsFinite()))
        {
            throw new InvalidDataException(
                "Item position payload contains a non-finite value.");
        }

        if (hasStatePayload &&
            ItemState == ItemState.Thrown &&
            !ThrowDirection.IsFinite())
        {
            throw new InvalidDataException(
                "Item throw direction contains a non-finite value.");
        }

        if (hasStatePayload && !HasValidInventoryLayout())
        {
            throw new InvalidDataException(
                $"Invalid inventory layout for slot {InventorySlot}.");
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) ||
            UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            ValidateTrackedValues();
        }
    }

    internal bool HasValidInventoryLayout()
    {
        if (ItemState != ItemState.InHand &&
            ItemState != ItemState.InInventory)
        {
            return true;
        }

        return InventorySlot >= -1 &&
               InventorySlot <=
                   ItemPacketLimits.MaxInventorySlotIndex &&
               (InventorySlot >= 0 ||
                !InLockedSlot && !IsDropped);
    }

    private void ValidateTrackedValues()
    {
        if (States == null)
            return;
        if (States.Count > ItemPacketLimits.MaxTrackedValues)
        {
            throw new InvalidDataException(
                $"Tracked-value count exceeds the limit: {States.Count}.");
        }

        foreach (KeyValuePair<string, object> state in States)
        {
            if (string.IsNullOrEmpty(state.Key) ||
                state.Key.Length >
                    ItemPacketLimits.MaxTrackedValueKeyLength)
            {
                throw new InvalidDataException(
                    "Tracked-value key is null, empty, or too long.");
            }

            switch (state.Value)
            {
                case bool:
                case int:
                case uint:
                    break;
                case float value when value.IsFinite():
                    break;
                case double value when
                    !double.IsNaN(value) &&
                    !double.IsInfinity(value):
                    break;
                case string value when
                    value.Length <=
                    ItemPacketLimits.MaxTrackedStringLength:
                    break;
                case Vector3 value when value.IsFinite():
                    break;
                case Quaternion value when value.IsFinite():
                    break;
                default:
                    throw new InvalidDataException(
                        $"Tracked value '{state.Key}' has an unsupported, " +
                        "null, non-finite, or oversized value.");
            }
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        InventorySlot = -1;
        InLockedSlot = false;
        IsDropped = false;
        UpdateType = (ItemUpdateType)reader.GetByte();
        ItemNetId = reader.GetUShort();

        if (UpdateType == ItemUpdateType.Destroy)
            return;

        ItemState = (ItemState)reader.GetByte();

        if (UpdateType.HasFlag(ItemUpdateType.Create))
        {
            PrefabName = reader.GetString(
                ItemPacketLimits.MaxPrefabNameLength);
            CreationRequestId = reader.GetUInt();
        }

        Player = reader.GetByte();

        bool hasStatePayload =
            UpdateType.HasFlag(ItemUpdateType.Create) ||
            UpdateType.HasFlag(ItemUpdateType.ItemState);
        bool hasPositionPayload =
            UpdateType.HasFlag(ItemUpdateType.Create) ||
            UpdateType.HasFlag(ItemUpdateType.ItemPosition) ||
            (hasStatePayload &&
                (ItemState == ItemState.Dropped ||
                 ItemState == ItemState.Thrown));

        if (hasPositionPayload)
        {
            ItemPosition = Vector3Serializer.Deserialize(reader);
            ItemRotation = QuaternionSerializer.Deserialize(reader);
        }

        if (hasStatePayload)
        {
            if (ItemState == ItemState.Thrown)
            {
                Multiplayer.LogDebug(() => $"ItemUpdateData.Deserialize() Item Thrown before: {ThrowDirection}");
                ThrowDirection = Vector3Serializer.Deserialize(reader);
                Multiplayer.LogDebug(() => $"ItemUpdateData.Deserialize() Item Thrown after: {ThrowDirection}");
            }
            else if (ItemState == ItemState.Attached)
            {
                CarNetId = reader.GetUShort();
                AttachedFront = reader.GetBool();
            }
            else if (ItemState == ItemState.InContainer)
            {
                ContainerNetId = reader.GetUShort();
                ContainerSlot = reader.GetInt();
            }
<<<<<<< HEAD

            if (ItemState == ItemState.InHand ||
                ItemState == ItemState.InInventory)
            {
                InventorySlot = reader.GetInt();
                InLockedSlot = reader.GetBool();
                IsDropped = reader.GetBool();
            }
=======
>>>>>>> 64b64ddac4c01653a5f35094f35d4762e08207d6
        }

        if (UpdateType.HasFlag(ItemUpdateType.Create) || UpdateType.HasFlag(ItemUpdateType.ObjectState))
        {
            int stateCount = reader.GetInt();
            if (stateCount < 0 ||
                stateCount > ItemPacketLimits.MaxTrackedValues)
            {
                throw new InvalidDataException(
                    $"Invalid tracked-value count: {stateCount}.");
            }

            if (stateCount > 0)
            {
                States = new Dictionary<string, object>(stateCount);
                for (int i = 0; i < stateCount; i++)
                {
                    string key = reader.GetString(
                        ItemPacketLimits.MaxTrackedValueKeyLength);
                    object value = DeserializeTrackedValue(reader);
                    States[key] = value;
                }
            }
        }
    }

    private void SerializeTrackedValue(NetDataWriter writer, object value)
    {
        if (value is bool boolValue)
        {
            writer.Put((byte)0);
            writer.Put(boolValue);
        }
        else if (value is int intValue)
        {
            writer.Put((byte)1);
            writer.Put(intValue);
        }
        else if (value is uint uintValue)
        {
            writer.Put((byte)2);
            writer.Put(uintValue);
        }
        else if (value is float floatValue)
        {
            writer.Put((byte)3);
            writer.Put(floatValue);
        }
        else if (value is string stringValue)
        {
            writer.Put((byte)4);
            writer.Put(stringValue);
        }
        else if (value is double doubleValue)
        {
            writer.Put((byte)5);
            writer.Put(doubleValue);
        }
        else if (value is Vector3 vectorValue)
        {
            writer.Put((byte)6);
            Vector3Serializer.Serialize(writer, vectorValue);
        }
        else if (value is Quaternion quaternionValue)
        {
            writer.Put((byte)7);
            QuaternionSerializer.Serialize(writer, quaternionValue);
        }
        else
        {
            throw new NotSupportedException(
                $"ItemUpdateData.SerializeTrackedValue({ItemNetId}, " +
                $"{PrefabName ?? ""}) Unsupported type for serialization: " +
                $"{value?.GetType().ToString() ?? "null"}");
        }
    }

    private object DeserializeTrackedValue(NetDataReader reader)
    {
        byte typeCode = reader.GetByte();
        switch (typeCode)
        {
            case 0: return reader.GetBool();
            case 1: return reader.GetInt();
            case 2: return reader.GetUInt();
            case 3: return reader.GetFloat();
            case 4:
                return reader.GetString(
                    ItemPacketLimits.MaxTrackedStringLength);
            case 5: return reader.GetDouble();
            case 6: return Vector3Serializer.Deserialize(reader);
            case 7: return QuaternionSerializer.Deserialize(reader);

            default:
                throw new NotSupportedException($"ItemUpdateData.DeserializeTrackedValue({ItemNetId}, {PrefabName ?? ""}) Unsupported type code for deserialization: {typeCode}");
        }
    }
}
