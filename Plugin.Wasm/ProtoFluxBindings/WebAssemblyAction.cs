using ProtoFlux.Core;
using FrooxEngine.ProtoFlux;
using ProtoFlux.Runtimes.Execution;
using FrooxEngine;
using Plugin.Wasm.ProtoFlux.NodeCompiler;
using System;

namespace Plugin.Wasm.ProtoFluxBindings;

using ActionNode = Plugin.Wasm.ProtoFlux.WebAssemblyAction;

public sealed class WebAssemblyAction : BaseWebAssemblyNode<ExecutionContext, ActionNode>, ISyncNodeOperation, INodeOperation
{
    /// <inheritdoc/>
    public override string NodeName => FunctionName ?? "Action";

    /// <inheritdoc/>
    protected override Type GetWasmNodeType(FunctionSignature signature)
        => WasmNodeJIT.GetActionType(signature);

    // ---- Action Flow Node ----

    /// <inheritdoc/>
    public ISyncOperation? MappedOperation { get; set; }

    IOperation? INodeOperation.MappedOperation
    {
        get => MappedOperation;
        set => MappedOperation = (ISyncOperation)value;
    }

    /// <inheritdoc/>
    public override int NodeOperationCount => base.NodeOperationCount + 1;

    protected override INodeOperation? GetOperationInternal(ref int index)
    {
        INodeOperation? operationInternal = base.GetOperationInternal(ref index);
        if (operationInternal is not null) return operationInternal;
        if (index == 0) return this;
        index--;
        return null;
    }

    /// <inheritdoc/>
    public override int NodeImpulseCount => base.NodeImpulseCount + 1;

    protected override ISyncRef? GetImpulseInternal(ref int index)
    {
        ISyncRef? impulseInternal = base.GetImpulseInternal(ref index);
        if (impulseInternal is not null) return impulseInternal;
        if (index == 0) return Next;
        index--;
        return null;
    }

    public readonly SyncRef<INodeOperation> Next;
}
