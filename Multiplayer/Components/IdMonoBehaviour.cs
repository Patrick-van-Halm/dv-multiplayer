using System;
using System.Collections.Generic;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components;

[DisallowMultipleComponent]
public abstract class IdMonoBehaviour<T, I> : MonoBehaviour where T : struct where I : MonoBehaviour
{
    private static readonly IdPool<T> idPool = new();
    private static readonly Dictionary<T, IdMonoBehaviour<T, I>> indexToObject = [];

    private T _netId;

    public T NetId {
        get => _netId;
        set {
            if (_netId.Equals(value))
                return;
            if (RecycleIds && (_netId as dynamic).CompareTo(default(T)) != 0)
                idPool.ReleaseId(_netId);
            Register(value);
        }
    }

    protected abstract bool IsIdServerAuthoritative { get; }
    protected virtual bool RecycleIds => true;

    protected static bool Get(T netId, out IdMonoBehaviour<T, I> obj)
    {
        if (indexToObject.TryGetValue(netId, out obj))
            return true;
        obj = null;
        if ((netId as dynamic).CompareTo(default(T)) != 0)
            Multiplayer.LogDebug(() => $"Got invalid NetId {netId} for {typeof(I).Name}{(NetworkLifecycle.Instance.IsProcessingPacket ? $" while processing packet\r\n{Environment.StackTrace}" : "")}");
        return false;
    }

    protected static bool TryGet(T netId, out IdMonoBehaviour<T, I> obj)
    {
        if (indexToObject.TryGetValue(netId, out obj))
            return true;

        obj = null;
        return false;
    }

    protected virtual void Awake()
    {
        if (IsIdServerAuthoritative && !NetworkLifecycle.Instance.IsHost())
            return;
        Register(idPool.NextId);
    }

    public void Register(T id)
    {
        if ((_netId as dynamic).CompareTo(default(T)) != 0)
        {
            if (indexToObject.TryGetValue(_netId, out IdMonoBehaviour<T, I> oldEntry) &&
                oldEntry == this)
            {
                indexToObject.Remove(_netId);
            }
        }

        _netId = id;
        if ((id as dynamic).CompareTo(default(T)) != 0)
            indexToObject[id] = this;
    }

    protected virtual void OnDestroy()
    {
        if (RecycleIds && (NetId as dynamic).CompareTo(default(T)) != 0)
            idPool.ReleaseId(NetId);
        if (indexToObject.TryGetValue(NetId, out IdMonoBehaviour<T, I> entry) && entry == this)
            indexToObject.Remove(NetId);

        if (UnloadWatcher.isUnloading)
        {
            idPool.Reset();
            indexToObject.Clear();
        }
    }
}
