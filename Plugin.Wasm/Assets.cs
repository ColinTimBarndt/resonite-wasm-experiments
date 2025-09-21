using System;
using System.Threading.Tasks;
using Elements.Assets;
using FrooxEngine;

namespace Plugin.Wasm;

/// <summary>
/// Provides a statically loaded WebAssembly module asset.
/// </summary>
[Category(["Assets"])]
public sealed class StaticWebAssemblyModule : StaticAssetProvider<WebAssemblyModule, DummyMetadata, SingleVariantDescriptor>
{
    /// <inheritdoc/>
    public override EngineAssetClass AssetClass => EngineAssetClass.Other;

    /// <inheritdoc/>
    protected override ValueTask<SingleVariantDescriptor> UpdateVariantDescriptor(DummyMetadata metadata, SingleVariantDescriptor currentDescriptor)
    {
        if (currentDescriptor is null) return new(new SingleVariantDescriptor(typeof(WebAssemblyModule)));
        Sync<Uri> _ = new();
        return new();
    }
}

/// <summary>
/// A WebAssembly Module asset.
/// </summary>
public sealed class WebAssemblyModule() : Asset<SingleVariantDescriptor>
{
    /// <summary>
    /// If loaded, holds the compiled WebAssembly module.
    /// </summary>
    public Wasmtime.Module? WasmModule { get; private set; } = null;

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
                //WasmModule = Wasmtime.Module.FromFile(WasmEngineProvider.Engine, file);
            }
            catch (Wasmtime.WasmtimeException error)
            {
                FailLoad(error.Message);
            }
        }
    }
}
