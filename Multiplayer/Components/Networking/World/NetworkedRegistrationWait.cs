using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

internal static class NetworkedRegistrationWait
{
    internal const int MaxFrames = 300;

    public static IEnumerator Until(
        UnityEngine.Object owner,
        Func<bool> isReady,
        Action register,
        string description)
    {
        for (int frame = 0; frame < MaxFrames; frame++)
        {
            if (owner == null)
                yield break;
            if (isReady())
            {
                register();
                yield break;
            }

            yield return null;
        }

        if (owner != null)
        {
            Multiplayer.LogWarning(
                $"Timed out waiting to register {description}.");
        }
    }
}
