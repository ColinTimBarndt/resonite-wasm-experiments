using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using Elements.Core;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

[NodeCategory("Web Assembly")]
[NodeName("Call Function", false)]
public abstract class CallFunction : ActionBreakableFlowNode<ExecutionContext>
{
    public abstract FunctionSignature Signature { get; }
    //public Wasmtime.Function? Function { get; private set; }

    protected abstract void InternalSetDelegate(Delegate? @delegate);

    /// <summary>
    /// Assigns <paramref name="func"/> to be called by this node if the signature is compatible.
    /// </summary>
    public bool TrySetFunction(Wasmtime.Function? func)
    {
        UniLog.Log($"Try set function {func} on {Signature}");
        if (func is null)
        {
            InternalSetDelegate(null);
            //Function = func;
            return true;
        }
        var @delegate = Signature.TryCreateDelegate(func);
        UniLog.Log($"Delegate: {@delegate}");
        if (@delegate is null) return false;
        InternalSetDelegate(@delegate);
        //Function = func;
        return true;
    }
}

public static class CallFunctionJit
{
    const bool SAVE_DLL = false;
    static PersistedAssemblyBuilder? assemblyBuilder = null;
    static readonly ModuleBuilder DynamicModuleBuilder = CreateDynamicModuleBuilder();

    private static ModuleBuilder CreateDynamicModuleBuilder()
    {
        var an = new AssemblyName("Plugin.Wasm.ProtoFlux.JIT");

        AssemblyBuilder asBuilder;
        if (SAVE_DLL)
        {
            assemblyBuilder = new PersistedAssemblyBuilder(an, typeof(object).Assembly);
            asBuilder = assemblyBuilder;
        }
        else
        {
            asBuilder = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndCollect);
        }
        return asBuilder.DefineDynamicModule("Plugin.Wasm.ProtoFlux.JIT");
    }

    private static ulong UniqueID = 0;

    private const TypeAttributes JIT_CLASS_ATTRIBUTES = TypeAttributes.Class | TypeAttributes.Public
        | TypeAttributes.Sealed | TypeAttributes.AutoLayout | TypeAttributes.AnsiClass;

    private const MethodAttributes JIT_CLASS_CTOR_ATTRIBUTES = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig;
    private const MethodAttributes PROTECTED_OVERRIDE = MethodAttributes.Family | MethodAttributes.Virtual;

    private const FieldAttributes DELEGATE_ATTRIBUTES = FieldAttributes.Public;
    private const FieldAttributes NODE_INPUT_ATTRIBUTES = FieldAttributes.Public;
    private const FieldAttributes NODE_OUTPUT_ATTRIBUTES = FieldAttributes.Public | FieldAttributes.InitOnly;

    private const CallingConventions STD_THIS = CallingConventions.Standard | CallingConventions.HasThis;

    private static readonly ConcurrentDictionary<FunctionSignature, Type> JitCache = [];

    public static Type GetSpecializedType(FunctionSignature signature) => JitCache.GetOrAdd(signature, GenerateClassSync);

    public static CallFunction NewSpecialized(FunctionSignature signature)
    {
        var item = JitCache.GetOrAdd(signature, GenerateClass);
        return (CallFunction)Activator.CreateInstance(item)!;
    }

    private static Type GenerateClassSync(FunctionSignature signature)
    {
        lock (JitCache)
        {
            return GenerateClass(signature);
        }
    }

    private static Type GenerateClass(FunctionSignature signature)
    {
        bool hasResults = signature.Results.Count != 0;
        Type delegateType = signature.GetDelegateType(out var resultType);

        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new Exception($"Couldn't find Invoke method for '{delegateType}'");

        var typeBuilder = DynamicModuleBuilder.DefineType($"CallFunction${UniqueID++:X4}{signature}", JIT_CLASS_ATTRIBUTES, typeof(CallFunction));

        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(typeof(NodeNameAttribute).GetConstructor([typeof(string), typeof(bool)])!, [typeBuilder.Name, false]));

        var delegateField = typeBuilder.DefineField("delegate", delegateType, DELEGATE_ATTRIBUTES);

        List<FieldBuilder> nodeInputs = [];
        for (int i = 0; i < signature.Parameters.Count; i++)
        {
            var type = signature.Parameters[i];
            var field = typeBuilder.DefineField($"Arg{i + 1}", GetNodeInputType(type), NODE_INPUT_ATTRIBUTES);
            nodeInputs.Add(field);
        }

        List<FieldBuilder> nodeOutputs = [];
        for (int i = 0; i < signature.Results.Count; i++)
        {
            var type = signature.Results[i];
            var field = typeBuilder.DefineField($"Res{i + 1}", GetNodeOutputType(type), NODE_OUTPUT_ATTRIBUTES);
            nodeOutputs.Add(field);
        }

        var ctor = typeBuilder.DefineConstructor(JIT_CLASS_CTOR_ATTRIBUTES, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = ctor.GetILGenerator();

            var baseCtor = typeof(CallFunction).GetConstructor(Type.EmptyTypes);
            // Base constructor
            if (baseCtor is not null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, baseCtor);
            }
            foreach (var outputField in nodeOutputs)
            {
                // outputField = new(this);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Newobj, outputField.FieldType.GetConstructor([typeof(Node)])!);
                il.Emit(OpCodes.Stfld, outputField);
            }
            il.Emit(OpCodes.Ret);
        }

        {
            var @override = typeof(CallFunction).GetMethod("Do", 0, BindingFlags.NonPublic | BindingFlags.Instance, [typeof(ExecutionContext)])!;
            var doMethod = typeBuilder.DefineMethod(@override.Name, PROTECTED_OVERRIDE, STD_THIS, typeof(bool), [typeof(ExecutionContext)]);

            var il = doMethod.GetILGenerator();
            var failLabel = il.DefineLabel();

            // try {
            il.BeginExceptionBlock();

            // push delegate onto stack
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, delegateField);
            // if (delegate is null) goto (failLabel)
            il.Emit(OpCodes.Dup);
            {
                var next = il.DefineLabel();
                il.Emit(OpCodes.Brtrue, next);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Leave, failLabel);
                il.MarkLabel(next);
            }

            // push inputs onto stack
            for (int i = 0; i < signature.Parameters.Count; i++)
            {
                var type = signature.Parameters[i];
                var field = nodeInputs[i];
                EvalInput(il, field, type);
            }

            // ... delegate.Invoke(...)
            il.EmitCall(OpCodes.Callvirt, invokeMethod, null);
            if (resultType is not null)
            {
                // (resultType) local0 = ...
                il.DeclareLocal(resultType);
                il.Emit(OpCodes.Stloc_0);
            }

            // } catch (WasmtimeException) {
            il.BeginCatchBlock(typeof(Wasmtime.WasmtimeException));
            il.Emit(OpCodes.Pop); // pop Exception
            //   return false;
            il.Emit(OpCodes.Leave, failLabel);
            // } (catch)
            il.EndExceptionBlock();

            if (resultType is not null)
            {
                if (signature.Results.Count == 1)
                {
                    var field = nodeOutputs[0];
                    WriteOutput(il, field, resultType, il => il.Emit(OpCodes.Ldloc_0));
                }
                else
                {
                    // Tuple
                    for (int i = 0; i < signature.Results.Count; i++)
                    {
                        var field = nodeOutputs[i];
                        var type = signature.Results[i];
                        WriteOutput(il, field, type, il =>
                        {
                            // value = local0.Item(i)
                            il.Emit(OpCodes.Ldloc_0);
                            il.Emit(OpCodes.Ldfld, resultType.GetField($"Item{i}") ?? throw new Exception("Failed to get tuple ItemN field"));
                        });
                    }
                }
            }

            // return true;
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);

            // return false;
            il.MarkLabel(failLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(doMethod, @override);
        }

        var signatureField = typeBuilder.DefineField("_signature", typeof(FunctionSignature), FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
        {
            var staticInit = typeBuilder.DefineTypeInitializer();
            var il = staticInit.GetILGenerator();
            BuildTypeArray(il, signature.Parameters);
            BuildTypeArray(il, signature.Results);
            il.Emit(OpCodes.Newobj, FunctionSignature.Constructor);
            il.Emit(OpCodes.Stsfld, signatureField);
            il.Emit(OpCodes.Ret);
        }

        {
            var @override = typeof(CallFunction).GetProperty("Signature", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
            var signatureGet = typeBuilder.DefineMethod(@override.Name, MethodAttributes.Public | MethodAttributes.Virtual, STD_THIS, typeof(FunctionSignature), Type.EmptyTypes);

            var il = signatureGet.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, signatureField);
            il.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(signatureGet, @override);
        }

        {
            var @override = typeof(CallFunction).GetMethod("InternalSetDelegate", 0, BindingFlags.NonPublic | BindingFlags.Instance, [typeof(Delegate)])!;
            var setDelegateMethod = typeBuilder.DefineMethod(@override.Name, PROTECTED_OVERRIDE, STD_THIS, @override.ReturnType, [typeof(Delegate)]);

            var il = setDelegateMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, delegateField);
            il.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(setDelegateMethod, @override);
        }

        var builtClass = typeBuilder.CreateType();

        if (SAVE_DLL)
        {
            assemblyBuilder!.Save("DEBUG.dll");
            UniLog.Log("Saved assembly DEBUG.dll");
        }

        return builtClass;
    }

    private static Type GetNodeInputType(Type type)
    {
        var wrapper = type.IsValueType ? typeof(ValueInput<>) : typeof(ObjectInput<>);
        return wrapper.MakeGenericType(type);
    }

    private static Type GetNodeOutputType(Type type)
    {
        var wrapper = type.IsValueType ? typeof(ValueOutput<>) : typeof(ObjectOutput<>);
        return wrapper.MakeGenericType(type);
    }

    private static readonly Type ExecutionContextExtensionsType = typeof(ExecutionContextExtensions);

    private static readonly MethodInfo EvaluateValueMethod, EvaluateObjectMethod, WriteValueMethod, WriteObjectMethod;

    static CallFunctionJit()
    {
        const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Static;
        var T0 = Type.MakeGenericMethodParameter(0);
        var ctx = typeof(ExecutionContext);

        var valInput = typeof(ValueInput<>).MakeGenericType(T0);
        var objInput = typeof(ObjectInput<>).MakeGenericType(T0);

        var evalVal = ExecutionContextExtensionsType.GetMethod("Evaluate", 1, FLAGS, [valInput, ctx, T0]);
        EvaluateValueMethod = evalVal ?? throw new Exception("Cannot find value Evaluate method");
        var evalObj = ExecutionContextExtensionsType.GetMethod("Evaluate", 1, FLAGS, [objInput, ctx, T0]);
        EvaluateObjectMethod = evalObj ?? throw new Exception("Cannot find object Evaluate method");

        var valOutput = typeof(ValueOutput<>).MakeGenericType(T0);
        var objOutput = typeof(ObjectOutput<>).MakeGenericType(T0);

        var writeVal = ExecutionContextExtensionsType.GetMethod("Write", 1, FLAGS, [valOutput, T0, ctx]);
        WriteValueMethod = writeVal ?? throw new Exception("Cannot find value Write method");

        var writeObj = ExecutionContextExtensionsType.GetMethod("Write", 1, FLAGS, [objOutput, T0, ctx]);
        WriteObjectMethod = writeObj ?? throw new Exception("Cannot find object Write method");
    }

    private static void EvalInput(ILGenerator il, FieldInfo field, Type type)
    {
        var method = (type.IsValueType ? EvaluateValueMethod : EvaluateObjectMethod).MakeGenericMethod(type);
        // input = this.(field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        // context
        il.Emit(OpCodes.Ldarg_1);
        // default
        if (type.IsValueType)
        {
            if (type == typeof(float)) il.Emit(OpCodes.Ldc_R4, 0f);
            else if (type == typeof(double)) il.Emit(OpCodes.Ldc_R8, 0d);
            else if (type == typeof(long) || type == typeof(ulong)) il.Emit(OpCodes.Ldc_I8, 0L);
            else il.Emit(OpCodes.Ldc_I4_0);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        // ExecutionContextExtensions.Evaluate<type>(input, context, default);
        il.EmitCall(OpCodes.Call, method, null);
    }

    private static void WriteOutput(ILGenerator il, FieldInfo field, Type type, Action<ILGenerator> loadValue)
    {
        var method = (type.IsValueType ? WriteValueMethod : WriteObjectMethod).MakeGenericMethod(type);
        // output = this.(field)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);

        // value = loadValue()
        loadValue(il);

        // context
        il.Emit(OpCodes.Ldarg_1);
        // ExecutionContextExtensions.Write<type>(output, value, context);
        il.EmitCall(OpCodes.Call, method, null);
    }

    private static readonly MethodInfo getTypeFromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
    private static void BuildTypeArray(ILGenerator il, IReadOnlyList<Type> values)
    {
        il.Emit(OpCodes.Ldc_I4, values.Count);
        il.Emit(OpCodes.Newarr, typeof(Type));
        for (int i = 0; i < values.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.EmitLoadInt(i);
            il.Emit(OpCodes.Ldtoken, values[i]);
            il.EmitCall(OpCodes.Call, getTypeFromHandle, null);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static readonly OpCode[] intImmediates = [
        OpCodes.Ldc_I4_M1, OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2, OpCodes.Ldc_I4_3,
        OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7, OpCodes.Ldc_I4_8
    ];
    private static void EmitLoadInt(this ILGenerator il, int value)
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
}
