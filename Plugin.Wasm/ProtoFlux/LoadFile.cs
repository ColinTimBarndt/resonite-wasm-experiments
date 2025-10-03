using System;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine.ProtoFlux;
using FrooxEngine.Store;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

[NodeCategory("Web Assembly")]
[NodeName("Load File", false)]
public class LoadFile : AsyncActionNode<FrooxEngineContext>
{
    // Inputs
    public ObjectInput<string?> WasmFile;

    // Outputs
    public readonly ObjectOutput<Uri?> ModuleAsset;

    public Continuation OnLoaded;
    public Continuation OnFailed;

    protected override async Task<IOperation> RunAsync(FrooxEngineContext context)
    {
        var file = WasmFile.Evaluate(context);
        if (file is null || !file.EndsWith(".wasm")) return OnFailed.Target;

        try
        {
            var url = await context.Engine.LocalDB.ImportLocalAssetAsync(file, LocalDB.ImportLocation.Copy).ConfigureAwait(continueOnCapturedContext: false);

            ModuleAsset.Write(url, context);

            return OnLoaded.Target;
        }
        catch (Exception error)
        {
            UniLog.Error(error.Message);
            return OnFailed.Target;
        }
    }
}