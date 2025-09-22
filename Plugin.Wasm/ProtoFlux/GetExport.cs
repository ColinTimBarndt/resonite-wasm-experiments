using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

[NodeCategory("Wasm")]
[NodeName("Get Export", false)]
public class GetExport : ExecutionNode<ExecutionContext>
{
    public readonly ObjectInput<WebAssemblyModule?> Module;
    public readonly ValueInput<int> Index;

    public readonly ObjectOutput<string> Name;
    public readonly ValueOutput<WebAssemblyExportType> Type;
    public readonly ObjectOutput<Wasmtime.Export> Export;

    public override bool CanBeEvaluated => true;

    public override void Evaluate(ExecutionContext context)
    {
        var module = Module.Evaluate(context)?.WasmModule;
        if (module is null) return;
        var index = Index.Evaluate(context);
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
        Export.Write(export, context);
    }
}