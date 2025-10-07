using System;
using Elements.Core;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

/// <summary>
/// A base class for all JIT-compiled WebAssembly Action ProtoFlux nodes.
/// </summary>
[NodeCategory("Web Assembly")]
public abstract class WebAssemblyAction : BaseWebAssemblyNode<ExecutionContext>, IExecutionOperationNode, ISyncOperation, IOperation
{
    // ---- ActionNode ----

    /// <inheritdoc/>
    public Node OwnerNode => this;

    /// <inheritdoc/>
    public override bool CanBeEvaluated => false;

    /// <inheritdoc/>
    public override void Evaluate(ExecutionContext context)
        => throw new NotSupportedException("Evaluation is not supported for action nodes.");

    /// <inheritdoc/>
    public ExecutionOperationHandler<T> GetHandler<T>() where T : ExecutionContext
        => new ExecutionOperationHandler<ExecutionContext>(Run);

    // ---- ActionBreakableFlowNode ----

    /// <summary>The continuation to run when the WebAssembly function could be executed.</summary>
    public Continuation Next;

    private IOperation? Run(ExecutionContext context) => Do(context) ? Next.Target : null;

    /// <summary>Perform the action and returns whether it was successful.</summary>
    protected abstract bool Do(ExecutionContext context);
}
