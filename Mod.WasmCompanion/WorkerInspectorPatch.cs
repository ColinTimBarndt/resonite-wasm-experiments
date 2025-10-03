using System;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.UIX;
using HarmonyLib;
using MonkeyLoader.Resonite;

namespace Mod.WasmInspector;

[HarmonyPatchCategory(nameof(WorkerInspectorPatch))]
[HarmonyPatch(typeof(SyncMemberEditorBuilder))]
internal sealed class WorkerInspectorPatch : ResoniteMonkey<WorkerInspectorPatch>
{
    public override bool CanBeDisabled => true;

    [HarmonyPostfix]
    [HarmonyPatch("Build", typeof(ISyncMember), typeof(string), typeof(FieldInfo), typeof(UIBuilder), typeof(float))]
    public static void BuildPostfix(ISyncMember member, string name, FieldInfo fieldInfo, UIBuilder ui)
    {
        // Some structs also expose the list interface
        if (!Enabled || member is ISyncList) return;

        var memberCasted = member.AsSyncElementStruct();
        if (memberCasted.HasValue) BuildStruct(memberCasted.Value, name, fieldInfo, ui);
    }

    private static void BuildStruct(SyncElementStruct @struct, string name, FieldInfo fieldInfo, UIBuilder ui)
    {
        ui.PushStyle();
        ui.Style.MinHeight = -1f;
        ui.VerticalLayout(4f);
        ui.Style.MinHeight = 24f;
        Text text = ui.Text(name + " (struct):", bestFit: true, null, parseRTF: false);
        colorX color = @struct.GetType().GetTypeColor().MulRGB(1.5f);
        InteractionElement.ColorDriver colorDriver = text.Slot.AttachComponent<Button>().ColorDrivers.Add();
        colorDriver.ColorDrive.Target = text.Color;
        Sync<colorX> normalColor = colorDriver.NormalColor;
        colorX a = RadiantUI_Constants.TEXT_COLOR;
        normalColor.Value = MathX.LerpUnclamped(in a, in color, 0.1f);
        colorDriver.HighlightColor.Value = RadiantUI_Constants.LABEL_COLOR;
        colorDriver.PressColor.Value = RadiantUI_Constants.HEADING_COLOR;
        text.Slot.AttachComponent<ReferenceProxySource>().Reference.Target = @struct.Value;

        ui.Style.MinHeight = -1f;
        ui.VerticalLayout(4f);

        Type editorType = WasmInspector.GetCustomStructEditorAttribute(fieldInfo) ?? WasmInspector.StructEditor!;
        Component editor = ui.Root.AttachComponent(editorType);
        ui.NestOut();
        ui.Style.MinHeight = 24f;
        ui.HorizontalLayout(4f);
        var typeField = ui.TextField(parseRTF: false);
        var addButton = ui.Button("Add");
        ui.NestOut();
        ui.NestOut();
        WasmInspector.SetupEditor!.Invoke(editor, [@struct.Value, addButton, typeField]);
        ui.PopStyle();
    }
}
