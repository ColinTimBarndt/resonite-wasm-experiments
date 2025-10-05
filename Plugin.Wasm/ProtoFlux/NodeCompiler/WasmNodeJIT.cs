//#define DEBUG_SAVE_DLL

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal static class WasmNodeJIT
{

#if DEBUG_SAVE_DLL
    private static PersistedAssemblyBuilder? assemblyBuilder = null;
#endif

    private static readonly ModuleBuilder DynamicModuleBuilder = CreateDynamicModuleBuilder();

    private static ModuleBuilder CreateDynamicModuleBuilder()
    {
        var an = new AssemblyName("Plugin.Wasm.ProtoFlux.JIT");

        AssemblyBuilder asBuilder;
#if DEBUG_SAVE_DLL
        assemblyBuilder = new PersistedAssemblyBuilder(an, typeof(object).Assembly);
        asBuilder = assemblyBuilder;
#else
        asBuilder = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndCollect);
#endif
        return asBuilder.DefineDynamicModule("Plugin.Wasm.ProtoFlux.JIT");
    }

    private static readonly ConcurrentDictionary<FunctionSignature, Type> ActionCache = [], FunctionCache = [];

    private static ulong UniqueID = 0;

    public static Type GetActionType(FunctionSignature signature) => ActionCache.GetOrAdd(signature, CompileActionNode);

    public static Type GetFunctionType(FunctionSignature signature) => FunctionCache.GetOrAdd(signature, CompileFunctionNode);

    private static readonly System.Threading.Lock jitLock = new();

    //static WasmNodeJIT()
    //{
    //    GenerateFlowNode(new FunctionSignature([typeof(int), typeof(double)], [typeof(long)]));
    //}

    private static Type CompileActionNode(FunctionSignature signature)
        => CompileNode(signature, typeof(WebAssemblyAction), new DelegatedBreakableExecuteMethodCompiler());

    private static Type CompileFunctionNode(FunctionSignature signature)
        => CompileNode(signature, typeof(WebAssemblyFunction), new DelegatedEvaluateMethodCompiler());

    private static Type CompileNode(FunctionSignature signature, Type baseNode, IRunMethodCompiler<DelegateState> compiler)
    {
        Type jit;
        lock (jitLock)
        {
            var builder = NodeBuilder<DelegateState>.Create(
                DynamicModuleBuilder,
                $"{baseNode.Name}${UniqueID++:X4}{signature}",
                baseNode,
                new(signature),
                compiler
            );
            builder.AddCompiler(new InternalSetDelegateMethodCompiler());
            builder.AddCompiler(new SignatureGetterCompiler());
            foreach (var parameter in signature.Parameters)
            {
                builder.DefineNodeInput(parameter);
            }
            foreach (var result in signature.Results)
            {
                builder.DefineNodeOutput(result);
            }
            jit = builder.Build();
        }
#if DEBUG_SAVE_DLL
        var filename = $"_DEBUG_{UniqueID - 1}.dll";
        assemblyBuilder!.Save(filename);
        Elements.Core.UniLog.Log($"Saved assembly '{filename}' for {jit.Name}");
#endif
        return jit;
    }
}