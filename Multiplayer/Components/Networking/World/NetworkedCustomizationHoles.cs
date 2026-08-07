using DV.Customization;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Managers.Client;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Utils;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedCustomizationHoles
{
    private const float HoleMatchRadius = 0.02f;
    private const float MaxClientInteractionDistance = 25f;
    private static bool applyingRemote;

    public static void OnHoleAdded(
        Customization customization,
        Vector3 localPosition,
        Vector3 localNormal)
    {
        Send(
            customization,
            CommonCustomizationHolePacket.HoleOperation.Add,
            localPosition,
            localPosition,
            localNormal);
    }

    public static Vector3 CaptureHolePosition(Collider hole)
    {
        return hole != null
            ? hole.transform.localPosition
            : Vector3.zero;
    }

    public static void OnHoleMoved(
        Customization customization,
        Collider hole,
        Vector3 localPosition,
        Vector3 localNormal,
        Vector3 previousPosition)
    {
        if (hole == null)
            return;

        Send(
            customization,
            CommonCustomizationHolePacket.HoleOperation.Move,
            previousPosition,
            localPosition,
            localNormal);
    }

    public static void OnHoleRemoved(
        Customization customization,
        bool removed,
        Vector3 previousPosition)
    {
        if (removed)
        {
            Send(
                customization,
                CommonCustomizationHolePacket.HoleOperation.Remove,
                previousPosition,
                previousPosition,
                Vector3.forward);
        }
    }

    private static void Send(
        Customization customization,
        CommonCustomizationHolePacket.HoleOperation operation,
        Vector3 previousPosition,
        Vector3 position,
        Vector3 normal)
    {
        NetworkLifecycle lifecycle = NetworkLifecycle.Instance;
        NetworkClient client = lifecycle?.Client;
        if (applyingRemote ||
            lifecycle == null ||
            lifecycle.IsProcessingPacket ||
            client == null ||
            !client.CanSendGameplayPackets ||
            customization == null)
        {
            return;
        }

        string destinationId = customization.GetIdentificationKey();
        if (!IsValidDestinationId(destinationId))
            return;

        ushort trainNetId = 0;
        if (customization is TrainCarCustomization trainCustomization)
        {
            NetworkedTrainCar.TryGetNetId(
                trainCustomization.TrainCar,
                out trainNetId);
        }

        NetworkLifecycle.Instance.Client.SendCustomizationHole(
            new CommonCustomizationHolePacket
            {
                Operation = operation,
                DestinationId = destinationId,
                TrainNetId = trainNetId,
                PreviousLocalPosition = previousPosition,
                LocalPosition = position,
                LocalNormal = normal,
            });
    }

    public static void ApplyRemote(CommonCustomizationHolePacket packet)
    {
        if (!TryResolve(packet, out Customization customization))
            return;

        applyingRemote = true;
        try
        {
            Collider existing = customization.FindHole(
                packet.PreviousLocalPosition,
                HoleMatchRadius);

            switch (packet.Operation)
            {
                case CommonCustomizationHolePacket.HoleOperation.Add:
                case CommonCustomizationHolePacket.HoleOperation.Move:
                    if (existing == null)
                    {
                        customization.AddHole(
                            packet.LocalPosition,
                            packet.LocalNormal);
                    }
                    else
                    {
                        customization.MoveHole(
                            existing,
                            packet.LocalPosition,
                            packet.LocalNormal);
                    }
                    break;

                case CommonCustomizationHolePacket.HoleOperation.Remove:
                    if (existing != null)
                        customization.RemoveHole(existing);
                    break;

                case CommonCustomizationHolePacket.HoleOperation.ReplaceAll:
                    customization.ClearHoles();
                    int count = Mathf.Min(
                        packet.Positions?.Length ?? 0,
                        packet.Normals?.Length ?? 0);
                    for (int i = 0; i < count; i++)
                    {
                        customization.AddHole(
                            packet.Positions[i],
                            packet.Normals[i]);
                    }
                    break;
            }
        }
        finally
        {
            applyingRemote = false;
        }
    }

    internal static bool ValidateClientPacket(
        CommonCustomizationHolePacket packet,
        Vector3 playerWorldPosition)
    {
        if (packet == null ||
            !System.Enum.IsDefined(
                typeof(CommonCustomizationHolePacket.HoleOperation),
                packet.Operation) ||
            packet.Operation ==
                CommonCustomizationHolePacket.HoleOperation.ReplaceAll ||
            string.IsNullOrEmpty(packet.DestinationId) ||
            packet.DestinationId.Length >
                CommonCustomizationHolePacket.MaxDestinationIdLength ||
            packet.Positions != null ||
            packet.Normals != null ||
            !packet.PreviousLocalPosition.IsFinite() ||
            !packet.LocalPosition.IsFinite() ||
            !packet.LocalNormal.IsFinite() ||
            !TryResolve(packet, out Customization customization))
        {
            return false;
        }

        return (playerWorldPosition - customization.transform.position)
                   .sqrMagnitude <=
               MaxClientInteractionDistance *
               MaxClientInteractionDistance;
    }

    public static IEnumerable<CommonCustomizationHolePacket>
        CreateFullSnapshots()
    {
        foreach (Customization customization in
                 Resources.FindObjectsOfTypeAll<Customization>())
        {
            if (customization == null ||
                !customization.gameObject.scene.IsValid())
            {
                continue;
            }

            string destinationId = customization.GetIdentificationKey();
            if (!IsValidDestinationId(destinationId))
                continue;

            ushort trainNetId = 0;
            if (customization is TrainCarCustomization trainCustomization)
            {
                NetworkedTrainCar.TryGetNetId(
                    trainCustomization.TrainCar,
                    out trainNetId);
            }

            Collider[] holes =
                customization.Holes.Where(hole => hole != null).ToArray();
            int snapshotCount = Mathf.Min(
                holes.Length,
                CommonCustomizationHolePacket.MaxHolesPerSnapshot);
            yield return new CommonCustomizationHolePacket
            {
                Operation =
                    CommonCustomizationHolePacket.HoleOperation.ReplaceAll,
                DestinationId = destinationId,
                TrainNetId = trainNetId,
                Positions = holes
                    .Take(snapshotCount)
                    .Select(hole => hole.transform.localPosition)
                    .ToArray(),
                Normals = holes
                    .Take(snapshotCount)
                    .Select(
                        hole =>
                            hole.transform.localRotation * Vector3.forward)
                    .ToArray(),
            };

            for (int index = snapshotCount;
                 index < holes.Length;
                 index++)
            {
                Vector3 position =
                    holes[index].transform.localPosition;
                yield return new CommonCustomizationHolePacket
                {
                    Operation =
                        CommonCustomizationHolePacket.HoleOperation.Add,
                    DestinationId = destinationId,
                    TrainNetId = trainNetId,
                    PreviousLocalPosition = position,
                    LocalPosition = position,
                    LocalNormal =
                        holes[index].transform.localRotation *
                        Vector3.forward,
                };
            }
        }
    }

    private static bool TryResolve(
        CommonCustomizationHolePacket packet,
        out Customization customization)
    {
        customization = null;
        if (packet.TrainNetId != 0 &&
            NetworkedTrainCar.TryGet(
                packet.TrainNetId,
                out TrainCar trainCar))
        {
            customization = trainCar.Customization;
        }

        return customization != null ||
             Customization.TryGetFromIdentificationKey(
                 packet.DestinationId,
                 out customization);
    }

    private static bool IsValidDestinationId(string destinationId)
    {
        return !string.IsNullOrEmpty(destinationId) &&
               destinationId.Length <=
               CommonCustomizationHolePacket.MaxDestinationIdLength;
    }

}
