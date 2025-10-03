using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Elements.Core;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;
using IInput = ProtoFlux.Core.IInput;

namespace Plugin.Wasm.ProtoFlux;

public abstract class GenericInput
{
    public abstract Type InputType { get; }
    public abstract IInput Input { get; }
    public abstract DataClass InputTypeClass { get; }
    public abstract IOutput Source { get; set; }
    public abstract FieldInfo Field { get; }
    public abstract object? Evaluate(ExecutionContext context);
    public object? DefaultValue => InputType.GetDefaultValue();

    private static readonly ConcurrentDictionary<Type, Func<GenericInput>> ConstructorCache = new();

    public static GenericInput New(Type type)
    {
        var create = ConstructorCache.GetOrAdd(type, type =>
        {
            Type wrappedType;
            if (type.IsUnmanaged())
                wrappedType = typeof(GenericValueInput<>).MakeGenericType(type);
            else
                wrappedType = typeof(GenericObjectInput<>).MakeGenericType(type);

            var ctor = wrappedType.GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException($"No empty constructor for {wrappedType}");
            var dynMethod = new DynamicMethod(string.Empty, wrappedType, Type.EmptyTypes, typeof(GenericInput));
            ILGenerator il = dynMethod.GetILGenerator();
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);

            return (Func<GenericInput>)dynMethod.CreateDelegate(typeof(Func<GenericInput>));
        });
        return create.Invoke();
    }
}

internal sealed class GenericValueInput<T> : GenericInput where T : unmanaged
{
    public ValueInput<T> TypedInput = new();

    public override Type InputType => typeof(T);
    public override IInput Input => TypedInput;
    public override DataClass InputTypeClass => DataClass.Value;
    public override IOutput Source
    {
        get => TypedInput.Source;
        set
        {
            if (value is null)
            {
                TypedInput.Source = null;
                return;
            }
            if (value is not IValueOutput<T> typedValue) throw new InvalidCastException($"{value.GetType()} -> {typeof(IValueOutput<T>)}");
            TypedInput.Source = typedValue;
        }
    }

    public override object? Evaluate(ExecutionContext context) => TypedInput.Evaluate(context);

    public override string ToString() => $"ValueInput<{typeof(T)}>({TypedInput.Source})";

    private static readonly FieldInfo StaticFieldInfo = typeof(ValueInput<T>).GetField("TypedInput")!;

    public override FieldInfo Field => StaticFieldInfo;
}

internal sealed class GenericObjectInput<T> : GenericInput
{
    public ObjectInput<T?> TypedInput = new();

    public override Type InputType => typeof(T);
    public override IInput Input => TypedInput;
    public override DataClass InputTypeClass => DataClass.Object;
    public override IOutput Source
    {
        get => TypedInput.Source;
        set
        {
            if (value is null)
            {
                TypedInput.Source = null;
                return;
            }
            if (value is not IObjectOutput<T?> typedValue) throw new InvalidCastException($"{value.GetType()} -> {typeof(IObjectOutput<T>)}");
            TypedInput.Source = typedValue;
        }
    }

    public override object? Evaluate(ExecutionContext context) => TypedInput.Evaluate(context);

    public override string ToString() => $"ObjectInput<{typeof(T)}>({TypedInput.Source})";

    private static readonly FieldInfo StaticFieldInfo = typeof(ObjectInput<T>).GetField("TypedInput")!;

    public override FieldInfo Field => StaticFieldInfo;
}
