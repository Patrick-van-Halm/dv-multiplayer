using DV.Customization;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;
using System.IO;

namespace Multiplayer.Components.Networking.Customization;

public enum CustomizationTargetKind : byte
{
    TrainCar,
    World,
    Storage,
    PlayerHouse,
    PaintStation,
}

public readonly struct CustomizationRef
{
    public CustomizationTargetKind Kind { get; }
    public ushort TrainCarNetId { get; }

    public CustomizationRef(CustomizationTargetKind kind, ushort trainCarNetId = 0)
    {
        Kind = kind;
        TrainCarNetId = trainCarNetId;
    }

    public static bool TryFrom(Customization customization, out CustomizationRef reference)
    {
        reference = default;
        if (customization == null)
            return false;

        if (customization is TrainCarCustomization trainCustomization)
        {
            ushort netId = trainCustomization.TrainCar?.GetNetId() ?? 0;
            if (netId == 0)
                return false;

            reference = new CustomizationRef(CustomizationTargetKind.TrainCar, netId);
            return true;
        }

        string key = customization.GetIdentificationKey();
        reference = key switch
        {
            ":global:" => new CustomizationRef(CustomizationTargetKind.World),
            ":storage:" => new CustomizationRef(CustomizationTargetKind.Storage),
            ":player_house:" => new CustomizationRef(CustomizationTargetKind.PlayerHouse),
            ":paint_station:" => new CustomizationRef(CustomizationTargetKind.PaintStation),
            _ => default,
        };

        return key is ":global:" or ":storage:" or ":player_house:" or ":paint_station:";
    }

    public bool TryResolve(out Customization customization)
    {
        customization = null;
        if (Kind == CustomizationTargetKind.TrainCar)
        {
            if (!NetworkedTrainCar.TryGet(TrainCarNetId, out NetworkedTrainCar networkedTrainCar))
                return false;

            customization = networkedTrainCar.TrainCar?.Customization;
            return customization != null;
        }

        string key = Kind switch
        {
            CustomizationTargetKind.World => ":global:",
            CustomizationTargetKind.Storage => ":storage:",
            CustomizationTargetKind.PlayerHouse => ":player_house:",
            CustomizationTargetKind.PaintStation => ":paint_station:",
            _ => null,
        };

        return key != null && Customization.TryGetFromIdentificationKey(key, out customization);
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write((byte)Kind);
        writer.Write(TrainCarNetId);
    }

    public static CustomizationRef Deserialize(BinaryReader reader)
    {
        return new CustomizationRef((CustomizationTargetKind)reader.ReadByte(), reader.ReadUInt16());
    }
}
