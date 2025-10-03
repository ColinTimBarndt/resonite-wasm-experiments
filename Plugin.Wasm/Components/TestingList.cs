using System;
using FrooxEngine;
using Plugin.Wasm.GenericCollections;

namespace Plugin.Wasm.Components;

[Category(["*TEST*"])]
public sealed class MixedFieldList : Component
{
    private readonly SyncRef LastField;
    private readonly SyncFieldStruct Struct;

    [SyncMethod(typeof(Action<Type>))]
    public void AddField(Type type)
    {
        LastField.Target = Struct.Add(type);
    }
}