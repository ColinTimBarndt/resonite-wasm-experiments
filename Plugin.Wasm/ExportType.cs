using Elements.Data;

namespace Plugin.Wasm;

[DataModelType]
public enum WebAssemblyExportType
{
    Table,
    Global,
    Memory,
    Function,
}
