namespace Plugin.Wasm;

internal class StoreData
{
    /// <summary>
    /// The "memory" export of the module.
    /// </summary>
    public Wasmtime.Memory? Memory;
}