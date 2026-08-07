using DV.CabControls;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Packets.Common;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

internal sealed class ControlIKHandEventRelay : MonoBehaviour
{
    private const float ScrollPulseRefreshInterval = 0.2f;
    private const float ExplicitInteractionDeduplicationWindow = 0.05f;

    private ControlImplBase control;
    private float nextScrollPulseTime;
    private float lastUseTime = float.NegativeInfinity;

    public static void Attach(ControlImplBase control)
    {
        if (control == null)
            return;

        control.gameObject
            .GetOrAddComponent<ControlIKHandEventRelay>()
            .Initialize(control);
    }

    public void Initialize(ControlImplBase value)
    {
        if (control != null || value == null)
            return;

        control = value;
        control.Grabbed += OnGrabbed;
        control.Ungrabbed += OnUngrabbed;
        control.Used += OnUsed;
        control.ValueChanged += OnValueChanged;
    }

    private void OnDestroy()
    {
        if (control == null)
            return;

        control.Grabbed -= OnGrabbed;
        control.Ungrabbed -= OnUngrabbed;
        control.Used -= OnUsed;
        control.ValueChanged -= OnValueChanged;
    }

    private void OnGrabbed(ControlImplBase _)
    {
        Send(ControlHandInteraction.Grab);
    }

    private void OnUngrabbed(ControlImplBase _)
    {
        Send(ControlHandInteraction.Ungrab);
    }

    private void OnUsed()
    {
        lastUseTime = Time.unscaledTime;
        Send(ControlHandInteraction.Use);
    }

    private void OnValueChanged(ValueChangedEventArgs _)
    {
        // LeverBase.Scroll ultimately updates ControlImplBase.Value through
        // RequestValueUpdate. Restrict the visual pulse to the local
        // MouseWheelHoverScroller so replicated and physical value changes do
        // not animate the hand.
        if (!control.IsHoverScrolled() ||
            control.IsGrabbed() ||
            Time.unscaledTime - lastUseTime <
                ExplicitInteractionDeduplicationWindow ||
            Time.unscaledTime < nextScrollPulseTime)
        {
            return;
        }

        nextScrollPulseTime =
            Time.unscaledTime + ScrollPulseRefreshInterval;
        Send(ControlHandInteraction.Use);
    }

    private void Send(ControlHandInteraction interaction)
    {
        if (control == null ||
            NetworkLifecycle.Instance.IsProcessingPacket ||
            NetworkLifecycle.Instance.Client == null)
        {
            return;
        }

        Vector3 targetPosition = control.transform.position;
        GameObject[] colliderObjects = control.InteractionColliderObjects;
        if (colliderObjects != null)
        {
            foreach (GameObject colliderObject in colliderObjects)
            {
                Collider interactionCollider =
                    colliderObject?.GetComponentInChildren<Collider>();
                if (interactionCollider == null)
                    continue;

                targetPosition = interactionCollider.bounds.center;
                break;
            }
        }

        NetworkLifecycle.Instance.Client.SendControlHandInteraction(
            interaction,
            NetworkedItem.ToNetworkPosition(targetPosition));
    }
}
