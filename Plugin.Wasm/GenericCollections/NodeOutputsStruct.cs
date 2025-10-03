using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using ProtoFlux.Core;

namespace Plugin.Wasm.GenericCollections;

public sealed class NodeOutputsStruct : SyncElementListStruct
{
    private static readonly ConcurrentDictionary<Type, Func<EmptySyncElement>> ConstructorCache = new();

    /// <inheritdoc/>
    protected override ISyncMember NewMember(Type type)
    {
        var create = ConstructorCache.GetOrAdd(type, type =>
        {
            Type wrappedType;
            if (type.IsUnmanaged())
                wrappedType = typeof(NodeValueOutput<>).MakeGenericType(type);
            else
                wrappedType = typeof(NodeObjectOutput<>).MakeGenericType(type);

            var ctor = wrappedType.GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException($"No empty constructor for {wrappedType}");
            var dynMethod = new DynamicMethod(string.Empty, wrappedType, Type.EmptyTypes, typeof(NodeOutputsStruct));
            ILGenerator il = dynMethod.GetILGenerator();
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);

            return (Func<EmptySyncElement>)dynMethod.CreateDelegate(typeof(Func<EmptySyncElement>));
        });
        return create.Invoke();
    }

    protected override Type GetType(int index)
    {
        var node = Parent as ProtoFluxNode;
        if (node is null || index >= node.NodeInputCount) return typeof(dummy);
        return node.NodeInstance?.GetOutputType(index) ?? typeof(dummy);
    }

    public new INodeOutput this[int index] => (INodeOutput)GetElement(index);

    public void EnsureTypedLayout(NodeMetadata meta)
    {
        EnsureTypedLayout(meta.FixedOutputs.Select(output => output.OutputType));
    }
}