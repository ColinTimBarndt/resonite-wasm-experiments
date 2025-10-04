using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal static partial class Compiler
{
    private static readonly OpCode[] intImmediates = [
        OpCodes.Ldc_I4_M1, OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2, OpCodes.Ldc_I4_3,
        OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7, OpCodes.Ldc_I4_8
    ];

    public static void EmitLoadI4(this ILGenerator il, int value)
    {
        if (value >= -1 && value <= 8)
        {
            il.Emit(intImmediates[value + 1]);
            return;
        }

        if (value >= -128 && value <= 127)
        {
            il.Emit(OpCodes.Ldc_I4_S, (byte)value);
            return;
        }

        il.Emit(OpCodes.Ldc_I4, value);
    }

    private static readonly MethodInfo getTypeFromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;

    public static void EmitNewTypeArray(this ILGenerator il, IReadOnlyList<Type> values)
    {
        il.Emit(OpCodes.Ldc_I4, values.Count);
        il.Emit(OpCodes.Newarr, typeof(Type));
        for (int i = 0; i < values.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.EmitLoadI4(i);
            il.Emit(OpCodes.Ldtoken, values[i]);
            il.EmitCall(OpCodes.Call, getTypeFromHandle, null);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    public static void EmitLoadDefault(this ILGenerator il, Type type)
    {
        if (type.IsValueType)
        {
            if (type == typeof(float)) il.Emit(OpCodes.Ldc_R4, 0f);
            else if (type == typeof(double)) il.Emit(OpCodes.Ldc_R8, 0d);
            else if (type == typeof(long) || type == typeof(ulong)) il.Emit(OpCodes.Ldc_I8, 0L);
            else if (type == typeof(Wasmtime.V128)) throw new NotImplementedException("V128"); // TODO: V128
            else il.Emit(OpCodes.Ldc_I4_0);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    /// <summary>
    /// Pushes the value of the given node input onto the stack.
    /// </summary>
    public static void EmitEvalNodeInput(this ILGenerator il, NodeInput input)
    {
        var method = Reflection.GetInputEvaluationMethod(input.Type);
        // input = this.(field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, input.Field);
        // context
        il.Emit(OpCodes.Ldarg_1);
        // default
        il.EmitLoadDefault(input.Type);
        // ExecutionContextExtensions.Evaluate<type>(input, context, default);
        il.EmitCall(OpCodes.Call, method, null);
    }

    /// <summary>
    /// Pushes the value of the given node argument onto the stack.
    /// </summary>
    public static void EmitReadNodeArgument(this ILGenerator il, NodeInput input)
    {
        var method = Reflection.GetArgumentReadMethod(input.Type);
        // index
        il.EmitLoadI4(input.Index);
        // context
        il.Emit(OpCodes.Ldarg_1);
        // ExecutionContextExtensions.(method)(index, context);
        il.EmitCall(OpCodes.Call, method, null);
    }

    public static void EmitInitializeNodeOutput(this ILGenerator il, NodeOutput output)
    {
        // (outputField) = new(this);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Newobj, output.Field.FieldType.GetConstructor([Reflection.NodeType])!);
        il.Emit(OpCodes.Stfld, output.Field);
    }

    public static void EmitWriteNodeOutput(this ILGenerator il, NodeOutput output, Action<ILGenerator> pushValue) {
        var method = Reflection.GetOutputWriteMethod(output.Type);
        // output = this.(field)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, output.Field);

        // value = pushValue()
        pushValue(il);

        // context
        il.Emit(OpCodes.Ldarg_1);
        // ExecutionContextExtensions.Write<type>(output, value, context);
        il.EmitCall(OpCodes.Call, method, null);
    }
}