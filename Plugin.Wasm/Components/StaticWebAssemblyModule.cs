using System.Threading.Tasks;
using Elements.Assets;
using FrooxEngine;

namespace Plugin.Wasm.Components;

/// <summary>
/// Provides a statically loaded WebAssembly module asset.
/// </summary>
[Category(["Web Assembly", "Assets"])]
public sealed class StaticWebAssemblyModule() : StaticAssetProvider<WebAssemblyModule, DummyMetadata, SingleVariantDescriptor>()
{
    /// <inheritdoc/>
    override public EngineAssetClass AssetClass => EngineAssetClass.Other;

    /// <inheritdoc/>
    override protected ValueTask<SingleVariantDescriptor> UpdateVariantDescriptor(DummyMetadata metadata, SingleVariantDescriptor currentDescriptor)
    {
        if (currentDescriptor is null) return new(new SingleVariantDescriptor(typeof(WebAssemblyModule)));
        return new();
    }
}
