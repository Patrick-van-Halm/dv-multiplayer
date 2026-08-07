using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using Multiplayer.Utils;
using System;
using System.IO;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common;

public class CommonCustomizationHolePacket : INetSerializable
{
    public const int MaxDestinationIdLength = 256;
    public const int MaxHolesPerSnapshot = 4096;

    public enum HoleOperation : byte
    {
        Add,
        Move,
        Remove,
        ReplaceAll,
    }

    public HoleOperation Operation { get; set; }
    public string DestinationId { get; set; }
    public ushort TrainNetId { get; set; }
    public Vector3 PreviousLocalPosition { get; set; }
    public Vector3 LocalPosition { get; set; }
    public Vector3 LocalNormal { get; set; }
    public Vector3[] Positions { get; set; }
    public Vector3[] Normals { get; set; }

    public void Serialize(NetDataWriter writer)
    {
        Validate();

        writer.Put((byte)Operation);
        writer.Put(DestinationId);
        writer.Put(TrainNetId);
        Vector3Serializer.Serialize(writer, PreviousLocalPosition);
        Vector3Serializer.Serialize(writer, LocalPosition);
        Vector3Serializer.Serialize(writer, LocalNormal);

        int count = Operation == HoleOperation.ReplaceAll
            ? Positions.Length
            : 0;
        writer.Put(count);
        for (int index = 0; index < count; index++)
        {
            Vector3Serializer.Serialize(writer, Positions[index]);
            Vector3Serializer.Serialize(writer, Normals[index]);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        Positions = null;
        Normals = null;
        Operation = (HoleOperation)reader.GetByte();
        DestinationId = reader.GetString(MaxDestinationIdLength);
        TrainNetId = reader.GetUShort();
        PreviousLocalPosition = Vector3Serializer.Deserialize(reader);
        LocalPosition = Vector3Serializer.Deserialize(reader);
        LocalNormal = Vector3Serializer.Deserialize(reader);

        int count = reader.GetInt();
        if (count < 0 || count > MaxHolesPerSnapshot)
        {
            throw new InvalidDataException(
                $"Invalid customization-hole count: {count}.");
        }
        if (Operation != HoleOperation.ReplaceAll && count != 0)
        {
            throw new InvalidDataException(
                "Only full customization snapshots may contain hole arrays.");
        }

        if (Operation == HoleOperation.ReplaceAll)
        {
            Positions = new Vector3[count];
            Normals = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                Positions[index] = Vector3Serializer.Deserialize(reader);
                Normals[index] = Vector3Serializer.Deserialize(reader);
            }
        }

        Validate();
    }

    private void Validate()
    {
        if (!Enum.IsDefined(typeof(HoleOperation), Operation))
        {
            throw new InvalidDataException(
                $"Invalid customization-hole operation: {Operation}.");
        }
        if (string.IsNullOrEmpty(DestinationId) ||
            DestinationId.Length > MaxDestinationIdLength)
        {
            throw new InvalidDataException(
                "Customization destination ID is null, empty, or too long.");
        }
        if (!PreviousLocalPosition.IsFinite() ||
            !LocalPosition.IsFinite() ||
            !LocalNormal.IsFinite())
        {
            throw new InvalidDataException(
                "Customization-hole payload contains a non-finite vector.");
        }

        if (Operation != HoleOperation.ReplaceAll)
        {
            if ((Positions?.Length ?? 0) != 0 ||
                (Normals?.Length ?? 0) != 0)
            {
                throw new InvalidDataException(
                    "Customization mutations cannot contain hole arrays.");
            }
            return;
        }

        if (Positions == null ||
            Normals == null ||
            Positions.Length != Normals.Length ||
            Positions.Length > MaxHolesPerSnapshot)
        {
            throw new InvalidDataException(
                "Customization snapshot arrays are missing, mismatched, " +
                "or too large.");
        }

        for (int index = 0; index < Positions.Length; index++)
        {
            if (!Positions[index].IsFinite() ||
                !Normals[index].IsFinite())
            {
                throw new InvalidDataException(
                    "Customization snapshot contains a non-finite vector.");
            }
        }
    }
}
