using ProtoFlux.Core;
using FrooxEngine.ProtoFlux;
using ProtoFlux.Runtimes.Execution;
using FrooxEngine;
using Plugin.Wasm.ProtoFlux.NodeCompiler;
using System;

namespace Plugin.Wasm.ProtoFluxBindings;

using FunctionNode = Plugin.Wasm.ProtoFlux.WebAssemblyFunction;

/// <summary>
/// FrooxEngine bindings for the corresponding WebAssembly Function ProtoFlux node.
/// </summary>
public sealed class WebAssemblyFunction : BaseWebAssemblyNode<ExecutionContext, FunctionNode>
{
    /// <inheritdoc/>
    public override string NodeName => FunctionName ?? "Function";

    /// <inheritdoc/>
    protected override Type GetWasmNodeType(FunctionSignature signature)
        => WasmNodeJIT.GetFunctionType(signature);
}
