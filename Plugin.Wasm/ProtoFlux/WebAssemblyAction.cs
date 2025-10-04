using System;
using Elements.Core;
using Plugin.Wasm.Components;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

[NodeCategory("Web Assembly")]
public abstract class WebAssemblyAction : ActionBreakableFlowNode<ExecutionContext>, IWebAssemblyNode
{
    public readonly GlobalRef<WebAssemblyInstance.FunctionExport?> Function;

    private void OnFunctionChanged(WebAssemblyInstance.FunctionExport? function, ExecutionContext ctx)
    {
        UniLog.Log($"OnFunctionChanged '{function?.Name}'");
        TrySetFunction(function?.Function);
    }

    /// <inheritdoc/>
    public abstract FunctionSignature Signature { get; }

    /// <summary>
    /// Sets the delegate which is used by the JIT-compiled class.
    /// The method does not ensure that the <paramref name="delegate"/>
    /// is of the correct type.
    /// </summary>
    protected abstract void InternalSetDelegate(Delegate? @delegate);

    /// <inheritdoc/>
    public bool TrySetFunction(Wasmtime.Function? func)
    {
        UniLog.Log($"Try set function {func} on {Signature}");
        if (func is null)
        {
            InternalSetDelegate(null);
            return true;
        }
        var @delegate = Signature.TryCreateDelegate(func);
        UniLog.Log($"Delegate: {@delegate}");
        if (@delegate is null) return false;
        InternalSetDelegate(@delegate);
        return true;
    }
}
