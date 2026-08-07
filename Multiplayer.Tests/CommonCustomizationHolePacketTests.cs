using LiteNetLib.Utils;
using Multiplayer.Networking.Packets.Common;
using System.IO;
using UnityEngine;
using Xunit;

namespace Multiplayer.Tests;

public class CommonCustomizationHolePacketTests
{
    [Fact]
    public void Deserialize_RejectsHoleCountBeforeAllocatingArrays()
    {
        var writer = new NetDataWriter();
        writer.Put((byte)CommonCustomizationHolePacket.HoleOperation.ReplaceAll);
        writer.Put("destination");
        writer.Put((ushort)0);
        PutVector(writer, Vector3.zero);
        PutVector(writer, Vector3.zero);
        PutVector(writer, Vector3.forward);
        writer.Put(
            CommonCustomizationHolePacket.MaxHolesPerSnapshot + 1);
        var packet = new CommonCustomizationHolePacket();

        Assert.Throws<InvalidDataException>(
            () => packet.Deserialize(
                new NetDataReader(writer.CopyData())));
        Assert.Null(packet.Positions);
        Assert.Null(packet.Normals);
    }

    [Fact]
    public void ReplaceAll_RoundTripsBoundedHoleArrays()
    {
        var expected = new CommonCustomizationHolePacket
        {
            Operation =
                CommonCustomizationHolePacket.HoleOperation.ReplaceAll,
            DestinationId = "destination",
            Positions = new[]
            {
                new Vector3(1f, 2f, 3f),
                new Vector3(4f, 5f, 6f),
            },
            Normals = new[]
            {
                Vector3.forward,
                Vector3.up,
            },
        };
        var writer = new NetDataWriter();

        expected.Serialize(writer);
        var result = new CommonCustomizationHolePacket();
        result.Deserialize(new NetDataReader(writer.CopyData()));

        Assert.Equal(expected.DestinationId, result.DestinationId);
        Assert.Equal(expected.Positions, result.Positions);
        Assert.Equal(expected.Normals, result.Normals);
    }

    [Fact]
    public void Deserialize_ClearsFullSnapshotArraysForIncrementalChanges()
    {
        var full = new CommonCustomizationHolePacket
        {
            Operation =
                CommonCustomizationHolePacket.HoleOperation.ReplaceAll,
            DestinationId = "destination",
            Positions = new[] { Vector3.zero },
            Normals = new[] { Vector3.forward },
        };
        var incremental = new CommonCustomizationHolePacket
        {
            Operation =
                CommonCustomizationHolePacket.HoleOperation.Add,
            DestinationId = "destination",
            LocalNormal = Vector3.forward,
        };
        var result = new CommonCustomizationHolePacket();
        var writer = new NetDataWriter();

        full.Serialize(writer);
        result.Deserialize(new NetDataReader(writer.CopyData()));
        writer.Reset();
        incremental.Serialize(writer);
        result.Deserialize(new NetDataReader(writer.CopyData()));

        Assert.Null(result.Positions);
        Assert.Null(result.Normals);
    }

    [Fact]
    public void Serialize_RejectsOversizedDestinationBeforeWriting()
    {
        var packet = new CommonCustomizationHolePacket
        {
            Operation =
                CommonCustomizationHolePacket.HoleOperation.Add,
            DestinationId = new string(
                'x',
                CommonCustomizationHolePacket.MaxDestinationIdLength + 1),
            LocalNormal = Vector3.forward,
        };
        var writer = new NetDataWriter();

        Assert.Throws<InvalidDataException>(
            () => packet.Serialize(writer));
        Assert.Equal(0, writer.Length);
    }

    [Fact]
    public void Serialize_RejectsNonFiniteMutationBeforeWriting()
    {
        var packet = new CommonCustomizationHolePacket
        {
            Operation =
                CommonCustomizationHolePacket.HoleOperation.Move,
            DestinationId = "destination",
            LocalPosition = new Vector3(float.PositiveInfinity, 0f, 0f),
            LocalNormal = Vector3.forward,
        };
        var writer = new NetDataWriter();

        Assert.Throws<InvalidDataException>(
            () => packet.Serialize(writer));
        Assert.Equal(0, writer.Length);
    }

    private static void PutVector(
        NetDataWriter writer,
        Vector3 value)
    {
        writer.Put(value.x);
        writer.Put(value.y);
        writer.Put(value.z);
    }
}
