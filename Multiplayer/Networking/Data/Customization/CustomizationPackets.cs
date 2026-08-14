using MPAPI.Interfaces.Packets;
using Multiplayer.Components.Networking.Customization;
using Multiplayer.Networking.Data.Items;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Multiplayer.Networking.Data.Customization;

internal static class CustomizationPacketIO
{
    public static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    public static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    public static void WriteQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }

    public static Quaternion ReadQuaternion(BinaryReader reader)
    {
        return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    public static void WriteCustomizationRef(BinaryWriter writer, CustomizationRef reference)
    {
        reference.Serialize(writer);
    }

    public static CustomizationRef ReadCustomizationRef(BinaryReader reader)
    {
        return CustomizationRef.Deserialize(reader);
    }

    public static void WriteItemUpdate(BinaryWriter writer, ItemUpdateData item)
    {
        writer.Write((byte)item.UpdateType);
        writer.Write(item.ItemNetId);
        writer.Write(item.PrefabName ?? string.Empty);
        writer.Write((byte)item.ItemState);
        WriteVector3(writer, item.ItemPosition);
        WriteQuaternion(writer, item.ItemRotation);
        WriteVector3(writer, item.ThrowDirection);
        writer.Write(item.Player);
        writer.Write(item.CarNetId);
        writer.Write(item.AttachedFront);

        int count = item.States?.Count ?? 0;
        writer.Write(count);
        if (count == 0)
            return;

        foreach (var state in item.States)
        {
            writer.Write(state.Key);
            WriteTrackedValue(writer, state.Value);
        }
    }

    public static ItemUpdateData ReadItemUpdate(BinaryReader reader)
    {
        ItemUpdateData item = new()
        {
            UpdateType = (ItemUpdateData.ItemUpdateType)reader.ReadByte(),
            ItemNetId = reader.ReadUInt16(),
            PrefabName = reader.ReadString(),
            ItemState = (Components.Networking.World.ItemState)reader.ReadByte(),
            ItemPosition = ReadVector3(reader),
            ItemRotation = ReadQuaternion(reader),
            ThrowDirection = ReadVector3(reader),
            Player = reader.ReadByte(),
            CarNetId = reader.ReadUInt16(),
            AttachedFront = reader.ReadBoolean(),
        };

        int count = reader.ReadInt32();
        if (count > 0)
        {
            item.States = new Dictionary<string, object>(count);
            for (int i = 0; i < count; i++)
                item.States[reader.ReadString()] = ReadTrackedValue(reader);
        }

        return item;
    }

    private static void WriteTrackedValue(BinaryWriter writer, object value)
    {
        switch (value)
        {
            case bool boolValue:
                writer.Write((byte)0);
                writer.Write(boolValue);
                break;
            case int intValue:
                writer.Write((byte)1);
                writer.Write(intValue);
                break;
            case uint uintValue:
                writer.Write((byte)2);
                writer.Write(uintValue);
                break;
            case float floatValue:
                writer.Write((byte)3);
                writer.Write(floatValue);
                break;
            case string stringValue:
                writer.Write((byte)4);
                writer.Write(stringValue);
                break;
            default:
                throw new NotSupportedException($"Unsupported customization tracked value type {value?.GetType()}");
        }
    }

    private static object ReadTrackedValue(BinaryReader reader)
    {
        return reader.ReadByte() switch
        {
            0 => reader.ReadBoolean(),
            1 => reader.ReadInt32(),
            2 => reader.ReadUInt32(),
            3 => reader.ReadSingle(),
            4 => reader.ReadString(),
            byte type => throw new InvalidDataException($"Unknown customization tracked value type {type}"),
        };
    }
}

public sealed class ClientboundCustomizationStatePacket : ISerializablePacket
{
    public List<GadgetPlacementState> Gadgets { get; set; } = [];
    public List<GadgetMountState> Mounts { get; set; } = [];
    public List<GadgetWireState> Wires { get; set; } = [];
    public List<CustomizationHoleState> Holes { get; set; } = [];
    public List<GadgetSnapState> Snaps { get; set; } = [];

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Gadgets.Count);
        foreach (var value in Gadgets)
            value.Serialize(writer);

        writer.Write(Mounts.Count);
        foreach (var value in Mounts)
            value.Serialize(writer);

        writer.Write(Wires.Count);
        foreach (var value in Wires)
            value.Serialize(writer);

        writer.Write(Holes.Count);
        foreach (var value in Holes)
            value.Serialize(writer);

        writer.Write(Snaps.Count);
        foreach (var value in Snaps)
            value.Serialize(writer);
    }

    public void Deserialize(BinaryReader reader)
    {
        Gadgets = ReadList(reader, GadgetPlacementState.Deserialize);
        Mounts = ReadList(reader, GadgetMountState.Deserialize);
        Wires = ReadList(reader, GadgetWireState.Deserialize);
        Holes = ReadList(reader, CustomizationHoleState.Deserialize);
        Snaps = ReadList(reader, GadgetSnapState.Deserialize);
    }

    private static List<T> ReadList<T>(BinaryReader reader, Func<BinaryReader, T> read)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > 100000)
            throw new InvalidDataException($"Invalid customization snapshot collection size {count}");

        List<T> result = new(count);
        for (int i = 0; i < count; i++)
            result.Add(read(reader));
        return result;
    }
}

public sealed class GadgetPlacementState
{
    public ItemUpdateData Item { get; set; }
    public CustomizationRef Target { get; set; }
    public Vector3 LocalPosition { get; set; }
    public Quaternion LocalRotation { get; set; }
    public bool IsOnGlass { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        CustomizationPacketIO.WriteItemUpdate(writer, Item);
        CustomizationPacketIO.WriteCustomizationRef(writer, Target);
        CustomizationPacketIO.WriteVector3(writer, LocalPosition);
        CustomizationPacketIO.WriteQuaternion(writer, LocalRotation);
        writer.Write(IsOnGlass);
    }

    public static GadgetPlacementState Deserialize(BinaryReader reader)
    {
        return new GadgetPlacementState
        {
            Item = CustomizationPacketIO.ReadItemUpdate(reader),
            Target = CustomizationPacketIO.ReadCustomizationRef(reader),
            LocalPosition = CustomizationPacketIO.ReadVector3(reader),
            LocalRotation = CustomizationPacketIO.ReadQuaternion(reader),
            IsOnGlass = reader.ReadBoolean(),
        };
    }
}

public sealed class GadgetMountState
{
    public ushort MountOwnerItemNetId { get; set; }
    public ushort MountedItemNetId { get; set; }
    public int MountIndex { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(MountOwnerItemNetId);
        writer.Write(MountedItemNetId);
        writer.Write(MountIndex);
    }

    public static GadgetMountState Deserialize(BinaryReader reader)
    {
        return new GadgetMountState
        {
            MountOwnerItemNetId = reader.ReadUInt16(),
            MountedItemNetId = reader.ReadUInt16(),
            MountIndex = reader.ReadInt32(),
        };
    }
}

public sealed class GadgetWireState
{
    public ushort GadgetAItemNetId { get; set; }
    public int PortAIndex { get; set; }
    public ushort GadgetBItemNetId { get; set; }
    public int PortBIndex { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(GadgetAItemNetId);
        writer.Write(PortAIndex);
        writer.Write(GadgetBItemNetId);
        writer.Write(PortBIndex);
    }

    public static GadgetWireState Deserialize(BinaryReader reader)
    {
        return new GadgetWireState
        {
            GadgetAItemNetId = reader.ReadUInt16(),
            PortAIndex = reader.ReadInt32(),
            GadgetBItemNetId = reader.ReadUInt16(),
            PortBIndex = reader.ReadInt32(),
        };
    }
}

public sealed class CustomizationHoleState
{
    public CustomizationRef Target { get; set; }
    public Vector3 LocalPosition { get; set; }
    public Vector3 LocalNormal { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        CustomizationPacketIO.WriteCustomizationRef(writer, Target);
        CustomizationPacketIO.WriteVector3(writer, LocalPosition);
        CustomizationPacketIO.WriteVector3(writer, LocalNormal);
    }

    public static CustomizationHoleState Deserialize(BinaryReader reader)
    {
        return new CustomizationHoleState
        {
            Target = CustomizationPacketIO.ReadCustomizationRef(reader),
            LocalPosition = CustomizationPacketIO.ReadVector3(reader),
            LocalNormal = CustomizationPacketIO.ReadVector3(reader),
        };
    }
}

public sealed class GadgetSnapState
{
    public ushort TargetGadgetItemNetId { get; set; }
    public ushort AttachedItemNetId { get; set; }
    public int SnapPointIndex { get; set; }
    public bool HasSlidingAnchor { get; set; }
    public Vector3 SlidingAnchorLocalPosition { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(TargetGadgetItemNetId);
        writer.Write(AttachedItemNetId);
        writer.Write(SnapPointIndex);
        writer.Write(HasSlidingAnchor);
        CustomizationPacketIO.WriteVector3(writer, SlidingAnchorLocalPosition);
    }

    public static GadgetSnapState Deserialize(BinaryReader reader)
    {
        return new GadgetSnapState
        {
            TargetGadgetItemNetId = reader.ReadUInt16(),
            AttachedItemNetId = reader.ReadUInt16(),
            SnapPointIndex = reader.ReadInt32(),
            HasSlidingAnchor = reader.ReadBoolean(),
            SlidingAnchorLocalPosition = CustomizationPacketIO.ReadVector3(reader),
        };
    }
}

public sealed class GadgetPlacePacket : ISerializablePacket
{
    public ushort GadgetItemNetId { get; set; }
    public CustomizationRef Target { get; set; }
    public Vector3 LocalPosition { get; set; }
    public Quaternion LocalRotation { get; set; }
    public bool IsOnGlass { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(GadgetItemNetId);
        Target.Serialize(writer);
        CustomizationPacketIO.WriteVector3(writer, LocalPosition);
        CustomizationPacketIO.WriteQuaternion(writer, LocalRotation);
        writer.Write(IsOnGlass);
    }

    public void Deserialize(BinaryReader reader)
    {
        GadgetItemNetId = reader.ReadUInt16();
        Target = CustomizationRef.Deserialize(reader);
        LocalPosition = CustomizationPacketIO.ReadVector3(reader);
        LocalRotation = CustomizationPacketIO.ReadQuaternion(reader);
        IsOnGlass = reader.ReadBoolean();
    }
}

public sealed class GadgetRemovePacket : ISerializablePacket
{
    public ushort GadgetItemNetId { get; set; }
    public bool ReparentToTrainCar { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(GadgetItemNetId);
        writer.Write(ReparentToTrainCar);
    }

    public void Deserialize(BinaryReader reader)
    {
        GadgetItemNetId = reader.ReadUInt16();
        ReparentToTrainCar = reader.ReadBoolean();
    }
}

public sealed class GadgetMountPacket : ISerializablePacket
{
    public ushort MountOwnerItemNetId { get; set; }
    public ushort MountedItemNetId { get; set; }
    public int MountIndex { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(MountOwnerItemNetId);
        writer.Write(MountedItemNetId);
        writer.Write(MountIndex);
    }

    public void Deserialize(BinaryReader reader)
    {
        MountOwnerItemNetId = reader.ReadUInt16();
        MountedItemNetId = reader.ReadUInt16();
        MountIndex = reader.ReadInt32();
    }
}

public sealed class GadgetUnmountPacket : ISerializablePacket
{
    public ushort MountOwnerItemNetId { get; set; }
    public int MountIndex { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(MountOwnerItemNetId);
        writer.Write(MountIndex);
    }

    public void Deserialize(BinaryReader reader)
    {
        MountOwnerItemNetId = reader.ReadUInt16();
        MountIndex = reader.ReadInt32();
    }
}

public sealed class GadgetWirePacket : ISerializablePacket
{
    public ushort GadgetAItemNetId { get; set; }
    public int PortAIndex { get; set; }
    public ushort GadgetBItemNetId { get; set; }
    public int PortBIndex { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(GadgetAItemNetId);
        writer.Write(PortAIndex);
        writer.Write(GadgetBItemNetId);
        writer.Write(PortBIndex);
    }

    public void Deserialize(BinaryReader reader)
    {
        GadgetAItemNetId = reader.ReadUInt16();
        PortAIndex = reader.ReadInt32();
        GadgetBItemNetId = reader.ReadUInt16();
        PortBIndex = reader.ReadInt32();
    }
}

public sealed class GadgetUnwirePacket : ISerializablePacket
{
    public ushort GadgetAItemNetId { get; set; }
    public int PortAIndex { get; set; }
    public ushort GadgetBItemNetId { get; set; }
    public int PortBIndex { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(GadgetAItemNetId);
        writer.Write(PortAIndex);
        writer.Write(GadgetBItemNetId);
        writer.Write(PortBIndex);
    }

    public void Deserialize(BinaryReader reader)
    {
        GadgetAItemNetId = reader.ReadUInt16();
        PortAIndex = reader.ReadInt32();
        GadgetBItemNetId = reader.ReadUInt16();
        PortBIndex = reader.ReadInt32();
    }
}

public sealed class CustomizationAddHolePacket : ISerializablePacket
{
    public CustomizationRef Target { get; set; }
    public Vector3 LocalPosition { get; set; }
    public Vector3 LocalNormal { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        Target.Serialize(writer);
        CustomizationPacketIO.WriteVector3(writer, LocalPosition);
        CustomizationPacketIO.WriteVector3(writer, LocalNormal);
    }

    public void Deserialize(BinaryReader reader)
    {
        Target = CustomizationRef.Deserialize(reader);
        LocalPosition = CustomizationPacketIO.ReadVector3(reader);
        LocalNormal = CustomizationPacketIO.ReadVector3(reader);
    }
}

public sealed class CustomizationMoveHolePacket : ISerializablePacket
{
    public CustomizationRef Target { get; set; }
    public Vector3 PreviousLocalPosition { get; set; }
    public Vector3 LocalPosition { get; set; }
    public Vector3 LocalNormal { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        Target.Serialize(writer);
        CustomizationPacketIO.WriteVector3(writer, PreviousLocalPosition);
        CustomizationPacketIO.WriteVector3(writer, LocalPosition);
        CustomizationPacketIO.WriteVector3(writer, LocalNormal);
    }

    public void Deserialize(BinaryReader reader)
    {
        Target = CustomizationRef.Deserialize(reader);
        PreviousLocalPosition = CustomizationPacketIO.ReadVector3(reader);
        LocalPosition = CustomizationPacketIO.ReadVector3(reader);
        LocalNormal = CustomizationPacketIO.ReadVector3(reader);
    }
}

public sealed class CustomizationRemoveHolePacket : ISerializablePacket
{
    public CustomizationRef Target { get; set; }
    public Vector3 LocalPosition { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        Target.Serialize(writer);
        CustomizationPacketIO.WriteVector3(writer, LocalPosition);
    }

    public void Deserialize(BinaryReader reader)
    {
        Target = CustomizationRef.Deserialize(reader);
        LocalPosition = CustomizationPacketIO.ReadVector3(reader);
    }
}

public sealed class SolderingMagazinePacket : ISerializablePacket
{
    public ushort ToolItemNetId { get; set; }
    public ushort SpoolItemNetId { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(ToolItemNetId);
        writer.Write(SpoolItemNetId);
    }

    public void Deserialize(BinaryReader reader)
    {
        ToolItemNetId = reader.ReadUInt16();
        SpoolItemNetId = reader.ReadUInt16();
    }
}

public sealed class SolderingDropEmptySpoolPacket : ISerializablePacket
{
    public ushort ToolItemNetId { get; set; }

    public void Serialize(BinaryWriter writer) => writer.Write(ToolItemNetId);
    public void Deserialize(BinaryReader reader) => ToolItemNetId = reader.ReadUInt16();
}

public sealed class GadgetSnapPacket : ISerializablePacket
{
    public GadgetSnapState State { get; set; } = new();

    public void Serialize(BinaryWriter writer) => State.Serialize(writer);
    public void Deserialize(BinaryReader reader) => State = GadgetSnapState.Deserialize(reader);
}

public sealed class GadgetUnsnapPacket : ISerializablePacket
{
    public ushort TargetGadgetItemNetId { get; set; }
    public ushort AttachedItemNetId { get; set; }
    public int SnapPointIndex { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(TargetGadgetItemNetId);
        writer.Write(AttachedItemNetId);
        writer.Write(SnapPointIndex);
    }

    public void Deserialize(BinaryReader reader)
    {
        TargetGadgetItemNetId = reader.ReadUInt16();
        AttachedItemNetId = reader.ReadUInt16();
        SnapPointIndex = reader.ReadInt32();
    }
}
