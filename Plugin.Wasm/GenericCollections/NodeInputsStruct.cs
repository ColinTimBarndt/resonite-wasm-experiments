using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection.Emit;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using ProtoFlux.Core;

namespace Plugin.Wasm.GenericCollections;

public sealed class NodeInputsStruct : SyncElementListStruct
{
    private static readonly ConcurrentDictionary<Type, Func<ISyncRef>> ConstructorCache = new();

    /// <inheritdoc/>
    protected override ISyncMember NewMember(Type type)
    {
        var create = ConstructorCache.GetOrAdd(type, type =>
        {
            Type outputType;
            if (type.IsUnmanaged())
                outputType = typeof(INodeValueOutput<>).MakeGenericType(type);
            else
                outputType = typeof(INodeObjectOutput<>).MakeGenericType(type);

            Type syncRefType = typeof(SyncRef<>).MakeGenericType(outputType);

            var ctor = syncRefType.GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException($"No empty constructor for {outputType}");
            var dynMethod = new DynamicMethod(string.Empty, syncRefType, Type.EmptyTypes, typeof(NodeInputsStruct));
            ILGenerator il = dynMethod.GetILGenerator();
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);

            return (Func<ISyncRef>)dynMethod.CreateDelegate(typeof(Func<ISyncRef>));
        });
        return create.Invoke();
    }

    protected override Type GetType(int index)
    {
        if (Parent is not ProtoFluxNode node || index >= node.NodeInputCount) return typeof(dummy);
        return node.NodeInstance?.GetInputType(index) ?? typeof(dummy);
    }

    public new ISyncRef this[int index] => (ISyncRef)GetElement(index);

    public void EnsureTypedLayout(NodeMetadata meta)
    {
        UniLog.Log($"EnsureTypedLayout NodeInputsStruct meta: {meta.Name} inputs: {meta.FixedInputCount}");
        EnsureTypedLayout(meta.FixedInputs.Select(input => input.InputType));
    }
}