using System;

namespace Multiplayer.Components.Networking.Customization;

public static class CustomizationSyncScope
{
    private static int remoteDepth;
    private static int rootActionDepth;

    public static bool IsApplyingRemote => remoteDepth > 0;
    public static bool IsApplyingRootAction => rootActionDepth > 0;

    public static IDisposable Remote(bool rootAction = false)
    {
        remoteDepth++;
        if (rootAction)
            rootActionDepth++;

        return new Scope(rootAction);
    }

    private sealed class Scope : IDisposable
    {
        private readonly bool rootAction;
        private bool disposed;

        public Scope(bool rootAction)
        {
            this.rootAction = rootAction;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (rootAction)
                rootActionDepth--;
            remoteDepth--;
        }
    }
}
