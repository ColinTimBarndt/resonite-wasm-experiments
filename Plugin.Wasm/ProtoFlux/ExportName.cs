using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

[NodeCategory("Wasm")]
[NodeName("Export Name", false)]
public class ExportName : ObjectFunctionNode<ExecutionContext, string?>
{
    public readonly ObjectInput<Wasmtime.Export?> Export;

    protected override string? Compute(ExecutionContext context)
    {
        var export = Export.Evaluate(context);
        return export?.Name;
    }
}