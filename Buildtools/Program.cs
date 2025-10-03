using System.Reflection;
using Mono.Cecil;
using Weavers;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

const string GAME_PATH = "/home/colin/.steam/steam/steamapps/common/Resonite/";
const string DLL_FILE = "/home/colin/Documents/Projects/software/resonite/wasm-experiments/Plugin.Wasm/bin/Release/net9.0/Plugin.Wasm.dll";

AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    var comma = args.Name.IndexOf(',');
    var name = args.Name[..comma];
    var path = Path.Combine(GAME_PATH, $"{name}.dll");
    return File.Exists(path) ? Assembly.LoadFile(path) : null;
};

DefaultAssemblyResolver resolver = new();
resolver.AddSearchDirectory(GAME_PATH);
var asm = AssemblyDefinition.ReadAssembly(DLL_FILE, new ReaderParameters
{
    AssemblyResolver = resolver,
});

var module = asm.MainModule;

var getExportCls = module.GetType("Plugin.Wasm.ProtoFlux.GetExport")!;
var loadFileCls = module.GetType("Plugin.Wasm.ProtoFlux.LoadFile")!;
Repro.Scan(getExportCls);
Repro.Scan(loadFileCls);

NodeWeaver nodeWeaver = new();
var moduleDef = typeof(NodeWeaver).BaseType!.GetProperty("ModuleDefinition")!;
moduleDef.SetValue(nodeWeaver, module);
nodeWeaver.LogInfo = msg => Console.WriteLine("[INFO] {0}", msg);
nodeWeaver.LogWarning = msg => Console.WriteLine("[WARN] {0}", msg);
nodeWeaver.LogError = msg => Console.WriteLine("[ERROR] {0}", msg);
nodeWeaver.Execute();

asm.Write("Modified.dll");
