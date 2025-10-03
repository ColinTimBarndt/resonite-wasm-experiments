using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection.Emit;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using ProtoFlux.Core;

namespace Plugin.Wasm.GenericCollections;

public abstract class SyncElementListStruct : SyncElementStruct, ISyncList
{
    public SyncElementListStruct()
    {
        ElementsAdded += (_, startIndex, count) => ListElementsAdded?.Invoke(this, startIndex, count);
        ElementsRemoved += (_, startIndex, count) => ListElementsRemoved?.Invoke(this, startIndex, count);
        ElementsRemoving += (_, startIndex, count) => ListElementsRemoving?.Invoke(this, startIndex, count);
    }

    private static readonly ConcurrentDictionary<Type, Func<ISyncRef>> ConstructorCache = new();

    public event SyncListEvent ListCleared;

    IEnumerable ISyncList.Elements => base.Elements;

    event SyncListElementsEvent ListElementsAdded;
    event SyncListElementsEvent ISyncList.ElementsAdded
    {
        add => ListElementsAdded += value;
        remove => ListElementsAdded -= value;
    }

    event SyncListElementsEvent ListElementsRemoved;
    event SyncListElementsEvent ISyncList.ElementsRemoved
    {
        add => ListElementsRemoved += value;
        remove => ListElementsRemoved -= value;
    }

    event SyncListElementsEvent ListElementsRemoving;
    event SyncListElementsEvent ISyncList.ElementsRemoving
    {
        add => ListElementsRemoving += value;
        remove => ListElementsRemoving -= value;
    }

    protected abstract Type GetType(int index);

    public ISyncMember AddElement() => AddElement(GetType(Count));

    public ISyncMember Add() => AddElement();
}