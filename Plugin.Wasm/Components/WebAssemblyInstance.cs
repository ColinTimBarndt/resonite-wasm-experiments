using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Elements.Core;
using Elements.Data;
using FrooxEngine;

namespace Plugin.Wasm.Components;

[Category(["Web Assembly"])]
public sealed class WebAssemblyInstance : Component
{
    // Fields
    public readonly RawOutput<bool> IsLoaded;
    public readonly AssetRef<WebAssemblyModule> Module;
    public readonly SyncBag<FunctionExport> FunctionExports;

    // Local state
    private Wasmtime.Store? Store;
    private Wasmtime.Instance? Instance;
    private WeakReference<Wasmtime.Module>? InstanceModule;

    /// <inheritdoc/>
    protected override void OnChanges()
    {
        var module = Module.Asset?.WasmModule;
        if (module is null)
        {
            if (Instance is null) return;
            Instance = null;
            InstanceModule = null;
            IsLoaded.Value = false;
            UpdateExports();
            Store?.Dispose();
            Store = null;
            return;
        }
        if (InstanceModule is null
            || !InstanceModule.TryGetTarget(out var instanceModule)
            || instanceModule != module
        )
        {
            // The referenced module has changed
            if (TryLoadModule(module))
                UpdateExports();
        }
    }

    private bool TryLoadModule(Wasmtime.Module module)
    {
        var engine = WasmEngineProvider.Engine;
        var newStore = new Wasmtime.Store(engine);
        try
        {
            Instance = new Wasmtime.Instance(newStore, module, []);
            Store = newStore;
            InstanceModule = new(module);
            newStore = null;
            IsLoaded.Value = true;
            UniLog.Log("Loaded WASM Module");
            return true;
        }
        catch (Wasmtime.WasmtimeException)
        {
            return false;
        }
        finally
        {
            newStore?.Dispose();
        }
    }

    protected override void SyncMemberChanged(IChangeable member)
    {
        base.SyncMemberChanged(member);
        if (member is Sync<string> name && member.Parent is FunctionExport export)
        {
            export.Function = this.Instance?.GetFunction(name.Value);
        }
    }

    private void UpdateExports()
    {
        var inst = Instance;
        if (inst is null)
        {
            foreach (var export in FunctionExports.Values)
                export.Function = null;
            return;
        }

        HashSet<string> Visited = [];

        foreach (var export in FunctionExports.Values)
        {
            var name = export.Name.Value;
            var func = inst.GetFunction(name);
            if (func is null) continue;
            Visited.Add(name);
            export.Function = func;
        }

        // Add missing exports
        foreach (var exportFunc in inst.GetFunctions())
        {
            if (Visited.Contains(exportFunc.Name)) continue;

            var exportMember = FunctionExports.Add();
            exportMember.Name.Value = exportFunc.Name;
            exportMember.Init(exportFunc.Function);
            UniLog.Log($"WASM Export Function: {exportFunc.Name}");
        }
    }

    /// <summary>
    /// Represents an export from the parent WebAssembly instance.
    /// </summary>
    public abstract class Export : SyncObject
    {
        public readonly Sync<string> Name;
    }

    /// <summary>
    /// Represents an exported function from the parent WebAssembly instance.
    /// </summary>
    public class FunctionExport : Export
    {
        public readonly SyncTypeList Parameters;
        public readonly SyncTypeList Results;

        /// <summary>
        /// An exported function, if the instance has a function export with a matching name.
        /// </summary>
        public Wasmtime.Function? Function { get; internal set; }

        /// <summary>
        /// Resets the type signature to the default, matching the exported function, if it exists.
        /// </summary>
        public void Init(Wasmtime.Function func)
        {
            Function = func;
            Parameters.Clear();
            Results.Clear();
            if (func is not null)
            {
                Parameters.AddRange(ValueKindMapper.MapTypes(func.Parameters));
                Results.AddRange(ValueKindMapper.MapTypes(func.Results));
            }
        }

        public FunctionSignature Signature => new([.. Parameters], [.. Results]);

        /// <inheritdoc/>
        public override string ToString() => Name.Value is not null ? $"{Name.Value} on {base.ToString()}" : base.ToString();
    }
}

internal static class ValueKindMapper
{
    public static Type[] MapTypes(IReadOnlyList<Wasmtime.ValueKind> values)
    {
        var types = new Type[values.Count];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = values[i] switch
            {
                Wasmtime.ValueKind.Int32 => typeof(int),
                Wasmtime.ValueKind.Int64 => typeof(long),
                Wasmtime.ValueKind.Float32 => typeof(float),
                Wasmtime.ValueKind.Float64 => typeof(double),
                Wasmtime.ValueKind.V128 => typeof(Wasmtime.V128),
                Wasmtime.ValueKind.FuncRef => typeof(SyncDelegate<System.Action>),
                Wasmtime.ValueKind.ExternRef => typeof(object),
                Wasmtime.ValueKind.AnyRef => typeof(object),
                _ => throw new NotImplementedException(),
            };
        }
        return types;
    }
}
