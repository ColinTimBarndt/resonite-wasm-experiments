using FrooxEngine.ProtoFlux;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

public interface IWebAssemblyNode : IExecutionNode<ExecutionContext>
{
    /// <summary>
    /// The function signature of the JIT-compiled node.
    /// </summary>
    FunctionSignature Signature { get; }

    /// <summary>
    /// Assigns <paramref name="func"/> to be called by this node if the signature is compatible.
    /// </summary>
    bool TrySetFunction(Wasmtime.Function? func);
}