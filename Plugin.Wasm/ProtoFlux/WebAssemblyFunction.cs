using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

/// <summary>
/// A base class for all JIT-compiled WebAssembly Function ProtoFlux nodes.
/// </summary>
[NodeCategory("Web Assembly")]
public abstract class WebAssemblyFunction : BaseWebAssemblyNode<ExecutionContext>
{
    /// <inheritdoc/>
    public override bool CanBeEvaluated => true;

    /// <inheritdoc/>
    public override void Evaluate(ExecutionContext context)
    {
        ComputeOutputs(context);
        context.PopInputs();
    }

    /// <summary>Computes the values of and writes to the node outputs.</summary>
    protected abstract void ComputeOutputs(ExecutionContext context);
}
