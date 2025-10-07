using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using MonkeyLoader.Resonite;

namespace Mod.WasmCompanion;

internal class WasmInspector : ResoniteMonkey<WasmInspector>
{
    public static Type? SyncElementStruct { get; private set; }
    public static Type? CustomStructEditorAttribute { get; private set; }
    public static Type? StructEditor { get; private set; }
    public static FieldInfo? StructEditorComponent { get; private set; }
    //public static MethodInfo? SyncElementStructElements { get; private set; }
    public static MethodInfo? SetupEditor { get; private set; }

    public const string SYNC_ELEMENT_STRUCT_TYPE = "Plugin.Wasm.GenericCollections.SyncElementStruct";
    public const string CUSTOM_STRUCT_EDITOR = "Plugin.Wasm.GenericCollections.CustomStructEditorAttribute";
    public const string STRUCT_EDITOR = "Plugin.Wasm.Components.StructEditor";

    protected override bool OnEngineReady()
    {
        if (!base.OnEngineReady()) return false;

        if (!GetType(SYNC_ELEMENT_STRUCT_TYPE, out var syncElementStruct)) return false;
        SyncElementStruct = syncElementStruct;

        if (!GetType(CUSTOM_STRUCT_EDITOR, out var customStructEditorAttribute)) return false;
        CustomStructEditorAttribute = customStructEditorAttribute;

        if (!GetType(STRUCT_EDITOR, out var structEditor)) return false;
        StructEditor = structEditor;

        var structEditorComponent = customStructEditorAttribute.GetField("StructEditorComponent");
        if (structEditorComponent is null || structEditorComponent.FieldType != typeof(Type))
        {
            Logger.Warn(() => $"Unable to find type field for '{customStructEditorAttribute}'.");
            return false;
        }
        StructEditorComponent = structEditorComponent;

        var setupEditor = AccessTools.Method(structEditor, "Setup", [syncElementStruct, typeof(Button), typeof(TextField)]);
        if (setupEditor is null)
        {
            Logger.Warn(() => $"Unable to find setup method for '{structEditor}'.");
            foreach (var method in AccessTools.GetDeclaredMethods(structEditor))
            {
                Logger.Info(() => $"Candidate: {method}");
            }
            return false;
        }
        SetupEditor = setupEditor;

        //var elements = AccessTools.PropertyGetter(syncElementStruct, "Elements");

        //if (elements is null || !elements.ReturnType.IsAssignableTo(typeof(IEnumerable<ISyncMember>)))
        //{
        //    Logger.Warn(() => $"Unable to find elements iterator '{elements}' for '{syncElementStruct}'.");
        //    return false;
        //}
        //SyncElementStructElements = elements;

        return true;
    }

    private static bool GetType(string fullName, [NotNullWhen(true)] out Type? type)
    {
        type = AccessTools.TypeByName(fullName);
        if (type is null)
        {
            Logger.Warn(() => $"Unable to find type '{fullName}'.");
            return false;
        }
        return true;
    }

    public static Type? GetCustomStructEditorAttribute(FieldInfo fieldInfo)
    {
        var attrib = fieldInfo?.GetCustomAttribute(WasmInspector.CustomStructEditorAttribute!);
        if (attrib is null) return null;
        return (Type)StructEditorComponent!.GetValue(attrib)!;
    }
}

internal readonly struct SyncElementStruct
{
    public readonly ConflictingSyncElement Value;

    //public IEnumerable<ISyncMember> Elements => (IEnumerable<ISyncMember>)WasmInspector.SyncElementStructElements!.Invoke(Value, null)!;

    internal SyncElementStruct(ConflictingSyncElement value)
    {
        Value = value;
    }
}

internal static class SyncElementStructExtensions
{
    public static SyncElementStruct? AsSyncElementStruct(this object? obj)
    {
        if (WasmInspector.SyncElementStruct is null) return null;
        if (obj is null || !obj.GetType().IsAssignableTo(WasmInspector.SyncElementStruct))
            return null;
        return new((ConflictingSyncElement)obj);
    }
}
