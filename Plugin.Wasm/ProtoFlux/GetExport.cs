using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

[NodeCategory("Web Assembly")]
[NodeName("Get Export", false)]
public sealed class GetExport : VoidNode<ExecutionContext>
{
    // Inputs
    public ObjectArgument<WebAssemblyModule?> Module;
    public ValueArgument<int> Index;

    // Outputs
    public readonly ObjectOutput<string> Name;
    public readonly ValueOutput<WebAssemblyExportType> Type;

    /// <inheritdoc/>
    public override bool CanBeEvaluated => true;

    protected override void ComputeOutputs(ExecutionContext context)
    {
        var module = Module.ReadObject(context)?.WasmModule;
        if (module is null) return;
        var index = Index.ReadValue(context);
        if (index < 0 || index >= module.Exports.Count) return;
        var export = module.Exports[index];
        Name.Write(export.Name, context);
        Type.Write(export switch
        {
            Wasmtime.TableExport => WebAssemblyExportType.Table,
            Wasmtime.GlobalExport => WebAssemblyExportType.Global,
            Wasmtime.MemoryExport => WebAssemblyExportType.Memory,
            Wasmtime.FunctionExport => WebAssemblyExportType.Function,
            _ => (WebAssemblyExportType)(-1),
        }, context);
    }
}