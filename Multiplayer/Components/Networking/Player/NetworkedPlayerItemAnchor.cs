using DV.Player;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

internal static class NetworkedPlayerItemAnchor
{
    private static int captureRequest;

    public static Vector3 PositionOffset { get; private set; } =
        new(0.2f, 1.5f, 0.4f);
    public static Quaternion RotationOffset { get; private set; } =
        Quaternion.identity;

    public static void Capture()
    {
        if (VRManager.IsVREnabled())
            return;

        PlayerManager.PlayerChanged -= ScheduleCapture;
        PlayerManager.PlayerChanged += ScheduleCapture;
        PlayerManager.CameraChanged -= ScheduleCapture;
        PlayerManager.CameraChanged += ScheduleCapture;

        ScheduleCapture();
    }

    private static void ScheduleCapture()
    {
        if (VRManager.IsVREnabled() ||
            CoroutineManager.Instance == null)
        {
            return;
        }

        int request = ++captureRequest;
        CoroutineManager.Instance.StartCoroutine(
            CaptureAtEndOfFrame(request));
    }

    private static IEnumerator CaptureAtEndOfFrame(int request)
    {
        yield return new WaitForEndOfFrame();

        if (request != captureRequest ||
            PlayerManager.PlayerTransform == null ||
            ItemPositionController.Instance == null ||
            ItemPositionController.Instance.itemAnchor == null)
        {
            yield break;
        }

        Transform player = PlayerManager.PlayerTransform;
        Transform anchor =
            ItemPositionController.Instance.itemAnchor;
        PositionOffset =
            player.InverseTransformPoint(anchor.position);
        RotationOffset =
            Quaternion.Inverse(player.rotation) *
            anchor.rotation;
        Multiplayer.LogDebug(
            () =>
                $"NetworkedPlayerItemAnchor.Capture() position offset: {PositionOffset}");
    }
}
