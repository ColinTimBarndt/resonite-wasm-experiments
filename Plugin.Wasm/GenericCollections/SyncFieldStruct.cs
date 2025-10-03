using System;
using System.Collections.Concurrent;
using System.Reflection.Emit;
using FrooxEngine;
using Plugin.Wasm.GenericCollections;

namespace Plugin.Wasm.GenericCollections;

public sealed class SyncFieldStruct : SyncElementStruct
{
    private static readonly ConcurrentDictionary<Type, Func<IField>> ConstructorCache = new();

    /// <inheritdoc/>
    protected override ISyncMember NewMember(Type type)
    {
        var create = ConstructorCache.GetOrAdd(type, type =>
        {
            Type wrappedType;
            if (type == typeof(Type))
                wrappedType = typeof(SyncType);
            else if (type.IsAssignableTo(typeof(IWorldElement)))
                wrappedType = typeof(SyncRef<>).MakeGenericType(type);
            else
                wrappedType = typeof(Sync<>).MakeGenericType(type);

            var ctor = wrappedType.GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException($"No empty constructor for {wrappedType}");
            var dynMethod = new DynamicMethod(string.Empty, wrappedType, Type.EmptyTypes, typeof(SyncFieldStruct));
            ILGenerator il = dynMethod.GetILGenerator();
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);

            return (Func<IField>)dynMethod.CreateDelegate(typeof(Func<IField>));
        });
        return create.Invoke();
    }

    public new IField this[int index] => (IField)GetElement(index);
}