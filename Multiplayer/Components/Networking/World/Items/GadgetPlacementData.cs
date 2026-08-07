using DV.Customization;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal sealed class GadgetPlacementData
{
    public string DestinationId { get; private set; }
    public ushort TrainNetId { get; private set; }
    public Vector3 LocalPosition { get; private set; }
    public Quaternion LocalRotation { get; private set; }

    public static bool TryParse(
        string serialized,
        out GadgetPlacementData placement)
    {
        placement = null;
        if (string.IsNullOrEmpty(serialized))
            return false;

        try
        {
            JObject data = JObject.Parse(serialized);
            string destinationId = data.Value<string>("destination");
            if (string.IsNullOrEmpty(destinationId) ||
                destinationId.Length > 256 ||
                !TryReadFiniteFloat(data, "px", out float px) ||
                !TryReadFiniteFloat(data, "py", out float py) ||
                !TryReadFiniteFloat(data, "pz", out float pz) ||
                !TryReadFiniteFloat(data, "rx", out float rx) ||
                !TryReadFiniteFloat(data, "ry", out float ry) ||
                !TryReadFiniteFloat(data, "rz", out float rz) ||
                !TryReadFiniteFloat(data, "rw", out float rw))
            {
                return false;
            }

            Quaternion rotation = new(rx, ry, rz, rw);
            if (!NetworkedItemDestinationAuthority
                    .IsValidRotation(rotation))
            {
                return false;
            }

            placement = new GadgetPlacementData
            {
                DestinationId = destinationId,
                TrainNetId = data.Value<ushort?>("trainNetId") ?? 0,
                LocalPosition = new Vector3(px, py, pz),
                LocalRotation = rotation,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public bool TryResolveDestination(
        out Customization destination,
        bool allowUnregisteredTrainFallback = true)
    {
        destination = null;
        if (TrainNetId != 0)
        {
            if (NetworkedTrainCar.TryGet(
                    TrainNetId,
                    out TrainCar trainCar))
            {
                if (trainCar?.Customization == null ||
                    trainCar.Customization.GetIdentificationKey() !=
                    DestinationId)
                {
                    return false;
                }

                destination = trainCar.Customization;
                return true;
            }

            if (!allowUnregisteredTrainFallback)
                return false;

            // The train is not registered yet. Use the stable destination key
            // until its network id becomes available.
        }

        return Customization.TryGetFromIdentificationKey(
            DestinationId,
            out destination);
    }

    private static bool TryReadFiniteFloat(
        JObject data,
        string key,
        out float value)
    {
        value = 0f;
        JToken token = data[key];
        if (token == null ||
            (token.Type != JTokenType.Float &&
             token.Type != JTokenType.Integer))
        {
            return false;
        }

        value = token.Value<float>();
        return value.IsFinite();
    }
}
