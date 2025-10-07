using System;
using System.Reflection.Emit;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal abstract class DelegatedRunMethodCompiler : BaseRunMethodCompiler<DelegateState>
{
    public sealed override void Build(ILGenerator il, NodeBuilder<DelegateState> node)
    {
        var failLabel = il.DefineLabel();
        var @delegate = node.State;

        // try {
        il.BeginExceptionBlock();

        // push delegate onto stack
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, @delegate.DelegateField!);
        // if (delegate is null) goto (failLabel)
        il.Emit(OpCodes.Dup);
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, next);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Leave, failLabel);
            il.MarkLabel(next);
        }

        // push evaluated/read inputs onto stack
        if (CanBeEvaluated)
            EmitPushArguments(il, node);
        else
            EmitPushInputs(il, node);

        // ... delegate.Invoke(...)
        il.EmitCall(OpCodes.Callvirt, @delegate.InvokeMethod, null);
        if (@delegate.ResultType is not null)
        {
            // (resultType) local0 = ...
            il.DeclareLocal(@delegate.ResultType);
            il.Emit(OpCodes.Stloc_0);
        }

        // } catch (WasmtimeException) {
        il.BeginCatchBlock(typeof(Wasmtime.WasmtimeException));
        il.Emit(OpCodes.Pop); // pop Exception

        //   return ...;
        il.Emit(OpCodes.Leave, failLabel);
        // } (catch)
        il.EndExceptionBlock();

        EmitWriteOutputs(il, node);

        // return (success);
        EmitReturn(il, true);

        // return (failure);
        il.MarkLabel(failLabel);
        EmitReturn(il, false);
    }

    /// <summary>
    /// Reads the result(s) from local index 0 and writes them to all outputs.
    /// </summary>
    protected static void EmitWriteOutputs(ILGenerator il, NodeBuilder<DelegateState> node)
    {
        var state = node.State;
        var resultType = state.ResultType;

        if (resultType is null) return;

        var outputs = node.NodeOutputs;
        if (outputs.Count == 1)
        {
            var output = outputs[0];
            il.EmitWriteNodeOutput(output, il => il.Emit(OpCodes.Ldloc_0));
            return;
        }

        // Results are in a Tuple
        for (int i = 0; i < outputs.Count; i++)
        {
            var output = outputs[i];
            il.EmitWriteNodeOutput(output, il =>
            {
                // value = local0.Item(i)
                il.Emit(OpCodes.Ldloc_0);
                var field = resultType.GetField($"Item{i + 1}") ?? throw new Exception($"Failed to get tuple Item{i + 1} property");
                il.Emit(OpCodes.Ldfld, field);
            });
        }
    }

    protected virtual void EmitReturn(ILGenerator il, bool success)
    {
        il.Emit(OpCodes.Ret);
    }
}

internal class DelegatedEvaluateMethodCompiler : DelegatedRunMethodCompiler
{
    public static readonly IRunMethodCompiler<DelegateState> Instance = new DelegatedEvaluateMethodCompiler();

    protected DelegatedEvaluateMethodCompiler() { }

    public sealed override bool CanBeEvaluated => true;

    public sealed override string MethodName => "ComputeOutputs";
}

internal class DelegatedExecuteMethodCompiler : DelegatedRunMethodCompiler
{
    public static readonly IRunMethodCompiler<DelegateState> Instance = new DelegatedExecuteMethodCompiler();

    protected DelegatedExecuteMethodCompiler() { }

    public sealed override bool CanBeEvaluated => false;

    public sealed override string MethodName => "Do";
}

internal class DelegatedBreakableExecuteMethodCompiler : DelegatedExecuteMethodCompiler
{
    new public static readonly IRunMethodCompiler<DelegateState> Instance = new DelegatedBreakableExecuteMethodCompiler();

    protected DelegatedBreakableExecuteMethodCompiler() { }

    public sealed override Type? ReturnType => typeof(bool);

    protected sealed override void EmitReturn(ILGenerator il, bool success)
    {
        il.Emit(success ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        base.EmitReturn(il, success);
    }
}
