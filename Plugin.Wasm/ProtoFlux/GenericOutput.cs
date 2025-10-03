using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Elements.Core;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

public abstract class GenericOutput
{
    public abstract Type OutputType { get; }
    public abstract IOutput Output { get; }
    public abstract DataClass OutputTypeClass { get; }
    public abstract FieldInfo Field { get; }
    public abstract void Write(object? value, ExecutionContext context);

    private static readonly ConcurrentDictionary<Type, Func<Node, GenericOutput>> ConstructorCache = new();

    public static GenericOutput New(Type type, Node owner)
    {
        var create = ConstructorCache.GetOrAdd(type, type =>
        {
            Type wrappedType;
            if (type.IsUnmanaged())
                wrappedType = typeof(GenericValueOutput<>).MakeGenericType(type);
            else
                wrappedType = typeof(GenericObjectOutput<>).MakeGenericType(type);

            Type[] args = [typeof(Node)];
            var ctor = wrappedType.GetConstructor(args) ?? throw new MissingMethodException($"No (Node owner) constructor for {wrappedType}");
            var dynMethod = new DynamicMethod(string.Empty, wrappedType, args, typeof(GenericOutput));
            ILGenerator il = dynMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);

            return (Func<Node, GenericOutput>)dynMethod.CreateDelegate(typeof(Func<Node, GenericOutput>));
        });
        return create.Invoke(owner);
    }
}

internal sealed class GenericValueOutput<T>(Node owner) : GenericOutput where T : unmanaged
{
    public readonly ValueOutput<T> TypedOutput = new(owner);
    public override Type OutputType => typeof(T);
    public override IOutput Output => TypedOutput;
    public override DataClass OutputTypeClass => DataClass.Value;
    public override void Write(object? value, ExecutionContext context)
    {
        if (value is not T typedValue) throw new ArgumentException("value of incorrect type");
        TypedOutput.Write(typedValue, context);
    }

    public override string ToString() => $"ValueOutput<{typeof(T)}>";

    private static readonly FieldInfo StaticFieldInfo = typeof(ValueOutput<T>).GetField("TypedOutput")!;

    public override FieldInfo Field => StaticFieldInfo;
}

internal sealed class GenericObjectOutput<T>(Node owner) : GenericOutput
{
    public readonly ObjectOutput<T?> TypedOutput = new(owner);
    public override Type OutputType => typeof(T);
    public override IOutput Output => TypedOutput;
    public override DataClass OutputTypeClass => DataClass.Value;
    public override void Write(object? value, ExecutionContext context)
    {
        if (value is null)
        {
            TypedOutput.Write(default, context);
            return;
        }
        if (value is not T typedValue) throw new ArgumentException("value of incorrect type");
        TypedOutput.Write(typedValue, context);
    }

    public override string ToString() => $"ObjectOutput<{typeof(T)}>";

    private static readonly FieldInfo StaticFieldInfo = typeof(ObjectOutput<T>).GetField("TypedOutput")!;

    public override FieldInfo Field => StaticFieldInfo;
}
