namespace Plugin.Wasm;

/// <summary>
/// Provides the global Wasmtime Engine instance.
/// </summary>
public static class WasmEngineProvider
{
    /// <summary>
    /// A global compilation and runtime environment for WebAssembly. An Engine is an object that can be shared concurrently across threads.
    /// </summary>
    /// <remarks>https://docs.rs/wasmtime/latest/wasmtime/struct.Engine.html</remarks>
    public static Wasmtime.Engine Engine { get; } = new();
}
