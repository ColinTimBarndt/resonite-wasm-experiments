using System;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;

namespace Plugin.Wasm;

/// <summary>
/// A WebAssembly Module asset.
/// </summary>
public sealed class WebAssemblyModule() : Asset<SingleVariantDescriptor>
{
    private Wasmtime.Module? _module;

    /// <summary>
    /// If loaded, holds the compiled WebAssembly module.
    /// </summary>
    public Wasmtime.Module? WasmModule
    {
        get => _module;
        set
        {
            _module = value;
            UniLog.Log("SET NEW MODULE");
            OnModuleChanged?.Invoke(this);
        }
    }

    internal void ReplaceModule(Wasmtime.Module newModule)
    {
        _module?.Dispose();
        WasmModule = newModule;
    }

    /// <summary>
    /// Fired when the WebAssembly module changes.
    /// This is not synchronous with the world!
    /// </summary>
    public event Action<WebAssemblyModule>? OnModuleChanged;

    /// <inheritdoc/>
    public override void Unload()
    {
        if (WasmModule is null) return;
        WasmModule.Dispose();
        WasmModule = null;
    }

    /// <inheritdoc/>
    protected override async Task LoadTargetVariant(SingleVariantDescriptor variant)
    {
        string file = await base.AssetManager.GatherAssetFile(base.AssetURL, 0f).ConfigureAwait(continueOnCapturedContext: false);
        if (file == null)
        {
            FailLoad("Could not gather asset file");
        }
        else
        {
            // TODO: If the gatherer has to download the asset, the module should be instantiated while streaming.
            try
            {
                WasmModule = Wasmtime.Module.FromFile(WasmEngineProvider.Engine, file);
            }
            catch (Wasmtime.WasmtimeException error)
            {
                FailLoad(error.Message);
            }
        }
    }
}
