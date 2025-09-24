using System;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine.ProtoFlux;
using FrooxEngine.Store;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux;

[NodeCategory("Wasm")]
[NodeName("Load File", false)]
public class LoadFile : AsyncActionNode<FrooxEngineContext>
{
    // Inputs
    public ObjectArgument<string?> WasmFile;

    // Outputs
    public readonly ObjectOutput<Uri?> ModuleAsset;

    public Continuation OnLoaded;
    public Continuation OnFailed;

    public LoadFile()
    {
        ModuleAsset = new(this);
    }

    protected override async Task<IOperation> RunAsync(FrooxEngineContext context)
    {
        var file = WasmFile.ReadObject(context);
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