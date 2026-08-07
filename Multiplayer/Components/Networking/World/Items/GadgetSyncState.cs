using DV.Common;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using DV.Items;
using DV.Player;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.World.Items;

internal sealed class GadgetSyncState : MonoBehaviour
{
    private const float SerializationInterval = 0.25f;

    public GadgetItem Item;
    internal bool IsRegistered { get; set; }
    private string cachedData = string.Empty;
    private float nextSerializationTime;
    private Coroutine placementRetry;

    public string GetPlacement()
    {
        GadgetBase gadget = Item?.Gadget;
        if (gadget == null || !gadget.IsLinked || gadget.Custom == null)
            return string.Empty;

        // Gadget placement is destination-local, matching GadgetItem.Place and the
        // game's save format. Local coordinates move with the train/customization
        // parent and therefore must not receive the global origin-shift offset.
        Transform transform = gadget.transform;
        ushort trainNetId = 0;
        if (gadget.Custom is TrainCarCustomization trainCustomization)
            NetworkedTrainCar.TryGetNetId(trainCustomization.TrainCar, out trainNetId);

        JObject data = new()
        {
            ["destination"] = gadget.Custom.GetIdentificationKey(),
            ["trainNetId"] = trainNetId,
            ["px"] = transform.localPosition.x,
            ["py"] = transform.localPosition.y,
            ["pz"] = transform.localPosition.z,
            ["rx"] = transform.localRotation.x,
            ["ry"] = transform.localRotation.y,
            ["rz"] = transform.localRotation.z,
            ["rw"] = transform.localRotation.w,
        };
        return data.ToString(Formatting.None);
    }

    public void SetPlacement(string serialized)
    {
        serialized ??= string.Empty;
        GadgetBase gadget = Item?.Gadget;

        if (string.IsNullOrEmpty(serialized))
        {
            if (placementRetry != null)
            {
                StopCoroutine(placementRetry);
                placementRetry = null;
            }
            if (gadget != null && gadget.IsLinked)
                gadget.Remove();
            return;
        }

        if (TryApplyPlacement(serialized))
        {
            // State dictionary enumeration is not an ordering contract. If gadget
            // data arrived first, reapply it now that train/customization links exist.
            SetGadgetData(cachedData);
            return;
        }

        if (placementRetry != null)
            StopCoroutine(placementRetry);
        placementRetry = StartCoroutine(RetryPlacement(serialized));
    }

    private bool TryApplyPlacement(string serialized)
    {
        GadgetBase gadget = Item?.Gadget;
        if (gadget == null)
            return false;

        if (!GadgetPlacementData.TryParse(
                serialized,
                out GadgetPlacementData placement) ||
            !placement.TryResolveDestination(
                out Customization destination))
        {
            return false;
        }

        if (gadget.IsLinked)
        {
            if (gadget.Custom == destination)
            {
                gadget.transform.localPosition =
                    placement.LocalPosition;
                gadget.transform.localRotation =
                    placement.LocalRotation;
                return true;
            }
            gadget.Remove(false);
        }

        GadgetItem.Place(
            destination,
            placement.LocalPosition,
            placement.LocalRotation,
            Item);
        Multiplayer.Log($"Applied gadget placement for {name} to {destination.GetIdentificationKey()}.");
        return true;
    }

    private IEnumerator RetryPlacement(string serialized)
    {
        const int maxFrames = 600;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;
            if (!TryApplyPlacement(serialized))
                continue;

            placementRetry = null;
            SetGadgetData(cachedData);
            yield break;
        }

        placementRetry = null;
        Multiplayer.LogWarning($"Timed out resolving gadget destination for {name}: {serialized}");
    }

    public string GetGadgetData()
    {
        if (Time.unscaledTime < nextSerializationTime)
            return cachedData;

        nextSerializationTime = Time.unscaledTime + SerializationInterval;
        JObject data = new();
        try
        {
            Item.Gadget.SaveDataRequested(data);
            cachedData = data.ToString(Formatting.None);
        }
        catch (Exception exception)
        {
            Multiplayer.LogWarning($"Could not serialize gadget state for {name}: {exception.Message}");
        }
        return cachedData;
    }

    public void SetGadgetData(string serialized)
    {
        cachedData = serialized ?? string.Empty;
        if (string.IsNullOrEmpty(cachedData) || Item?.Gadget == null)
            return;

        try
        {
            JObject data = JObject.Parse(cachedData);
            Item.Gadget.SaveDataLoaded(data);
            Item.Gadget.AfterSaveDataLoaded(data);
        }
        catch (Exception exception)
        {
            Multiplayer.LogWarning($"Could not apply gadget state for {name}: {exception.Message}");
        }
    }
}
