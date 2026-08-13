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

        return new Scope(rootAction, true);
    }

    public static IDisposable LocalRoot()
    {
        rootActionDepth++;
        return new Scope(true, false);
    }

    private sealed class Scope : IDisposable
    {
        private readonly bool rootAction;
        private readonly bool remote;
        private bool disposed;

        public Scope(bool rootAction, bool remote)
        {
            this.rootAction = rootAction;
            this.remote = remote;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (rootAction)
                rootActionDepth--;
            if (remote)
                remoteDepth--;
        }
    }
}
