using Elements.Data;

namespace Plugin.Wasm;

/// <summary>
/// The type of a WebAssembly export, used for ProtoFlux.
/// </summary>
/// <seealso cref="Wasmtime.Export"/>
[DataModelType]
public enum WebAssemblyExportType
{
    /// <seealso cref="Wasmtime.TableExport"/>
    Table,
    /// <seealso cref="Wasmtime.GlobalExport"/>
    Global,
    /// <seealso cref="Wasmtime.MemoryExport"/>
    Memory,
    /// <seealso cref="Wasmtime.FunctionExport"/>
    Function,
}
