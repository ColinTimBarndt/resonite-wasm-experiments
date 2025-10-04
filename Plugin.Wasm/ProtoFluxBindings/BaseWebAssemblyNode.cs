using System;
using ProtoFlux.Core;
using Elements.Core;
using FrooxEngine.ProtoFlux;
using Plugin.Wasm.GenericCollections;
using Plugin.Wasm.Components;
using WebAssemblyActionNode = Plugin.Wasm.ProtoFlux.WebAssemblyAction;
using Plugin.Wasm.ProtoFlux.NodeCompiler;
using FrooxEngine;
using FrooxEngine.ProtoFlux.Runtimes.Execution;
using ExecutionContext = ProtoFlux.Runtimes.Execution.ExecutionContext;
using System.Reflection;
using Plugin.Wasm.ProtoFlux;

namespace Plugin.Wasm.ProtoFluxBindings;

using FunctionExport = WebAssemblyInstance.FunctionExport;
using FunctionExportGlobal = IGlobalValueProxy<WebAssemblyInstance.FunctionExport>;

[Category("ProtoFlux/Runtimes/Execution/Nodes/Web Assembly")]
public abstract class BaseWebAssemblyNode<C, W> : ExecutionNode<C>
where C : ExecutionContext
where W : class, IWebAssemblyNode
{
    private FunctionSignature _functionSignature = new([], []);

    public override Type NodeType => GetWasmNodeType(_functionSignature);

    public W? TypedNodeInstance { get; private set; }

    /// <inheritdoc/>
    public override INode? NodeInstance => TypedNodeInstance;

    /// <summary>
    /// Gets or compiles the class of a ProtoFlux node
    /// which can hold a WebAssembly function with the specified signature.
    /// </summary>
    protected abstract Type GetWasmNodeType(FunctionSignature signature);

    /// <inheritdoc/>
    public override N Instantiate<N>()
    {
        UniLog.Log("Instantiate", true);

        if (TypedNodeInstance != null)
            throw new InvalidOperationException("Node has already been instantiated");

        var node = (W?)Activator.CreateInstance(GetWasmNodeType(_functionSignature))
            ?? throw new Exception("Activator instance is null");
        var func = _currentFunctionProxy?.Value?.Function;
        if (func is not null)
        {
            node.TrySetFunction(func);
        }

        UniLog.Log($"Instance: {node}");

        TypedNodeInstance = node;

        //OnInstantiated();

        return (node as N)!;
    }

    // public override void BuildContentUI(ProtoFluxNodeVisual visual, UIBuilder ui)
    // {
    //     UniLog.Log($"CallFunction::BuildContentUI");
    //     base.BuildContentUI(visual, ui);
    //     //SyncMemberEditorBuilder.Build(Function, "Function", typeof(CallFunction).GetField("Function")!, ui);
    //     ui.Empty();
    //     ui.Nest();
    //     var slot = ui.Empty();
    //     var rect = ui.CurrentRect;
    //     rect.OffsetMin.Value = new float2(16f, 0f);
    //     rect.OffsetMax.Value = new float2(-16f, 0f);
    //     slot.AttachComponent<Button>();
    //     var receiver = slot.AttachComponent<ReferenceReceiver<WebAssemblyInstance.FunctionExport>>();
    //     receiver.Reference.Target = Function;
    //     ui.NestOut();
    // }

    /// <inheritdoc/>
    protected override void AssociateInstanceInternal(INode node)
    {
        UniLog.Log($"AssociateInstanceInternal {node}", true);
        if (node is W typedNode)
        {
            TypedNodeInstance = typedNode;
            if (typedNode.Signature.Equals(_functionSignature)) return;

            // New function signature
            _functionSignature = typedNode.Signature;
            if (!typedNode.TrySetFunction(_currentFunctionProxy?.Value?.Function))
            {
                typedNode.TrySetFunction(null);
            }
            EnsureTypedLayout();

            //OnInstantiated();
        }
        else
            throw new ArgumentException("Node instance is not of type " + typeof(WebAssemblyActionNode));
    }

    private void EnsureTypedLayout()
    {
        var meta = NodeMetadataHelper.GetMetadata(NodeType);
        Inputs.EnsureTypedLayout(meta);
        Outputs.EnsureTypedLayout(meta);
    }

    /// <inheritdoc/>
    protected override void OnAwake()
    {
        base.OnAwake();
        var target = Function.Target;
        _currentFunctionProxy = target;
        if (target is null) return;
        target.OnValueChanged += OnFunctionGlobalChanged;
    }

    /// <inheritdoc/>
    protected override void OnAttach()
    {
        UniLog.Log("CallFunction OnAttach");
        EnsureTypedLayout();
        OnFunctionChanged(_currentFunctionProxy?.Value);
        base.OnAttach();
    }

    /// <inheritdoc/>
    protected override void SyncMemberChanged(IChangeable member)
    {
        base.SyncMemberChanged(member);
        if (member != Function || Function.Target == _currentFunctionProxy) return;

        if (_currentFunctionProxy is not null)
            _currentFunctionProxy.OnValueChanged -= OnFunctionGlobalChanged;
        _currentFunctionProxy = Function.Target;
        _currentFunctionProxy.OnValueChanged += OnFunctionGlobalChanged;
    }

    private FunctionExportGlobal? _currentFunctionProxy;

    private void OnFunctionGlobalChanged(IGlobalValueProxy changeable)
    {
        UniLog.Log($"OnFunctionChanged {changeable}");
        if (changeable is not FunctionExportGlobal globalFunc) return;

        OnFunctionChanged(globalFunc.Value);
    }

    private void OnFunctionChanged(FunctionExport? func)
    {
        UniLog.Log($"CallFunction OnFunctionChanged '{func?.Name}'");
        if (func is null)
        {
            FunctionName = null;
            UpdateVisual();
            return;
        }
        FunctionName = func.Name.Value;
        var sig = func.Signature;
        if (_functionSignature.Equals(sig))
        {
            UpdateVisual();
            return;
        }
        UniLog.Log($"Mark for rebuild; {sig}");
        _functionSignature = func.Signature;
        ClearInstance();
        //Instantiate<CallFunctionNode>();
        UpdateVisual();
        World.ProtoFlux.RegisterDirtyNode(this);
        //World.ProtoFlux.RegisterDirtyGroup(this.Group);
        //World.ProtoFlux.ScheduleGroupRebuild(this.Group);
    }

    private void UpdateVisual()
    {
        if (!World.IsAuthority) return;
        if (this.HasActiveVisual())
        {
            ProtoFluxVisualHelper.RemoveVisual(this);
            RunInUpdates(3, delegate
            {
                if (!this.IsDestroyed)
                {
                    EnsureTypedLayout();
                    ProtoFluxVisualHelper.EnsureVisual(this);
                }
            });
        }
    }

    /// <inheritdoc/>
    public override void ClearInstance() => TypedNodeInstance = null;

    /// <inheritdoc/>
    public override int NodeInputCount => base.NodeInputCount + Inputs.Count;

    /// <inheritdoc/>
    public override int NodeOutputCount => base.NodeOutputCount + Outputs.Count;

    /// <inheritdoc/>
    protected override ISyncRef? GetInputInternal(ref int index)
    {
        var @base = base.GetInputInternal(ref index);
        if (@base != null) return @base;
        if (index >= 0 && index < Inputs.Count) return Inputs[index];
        index -= Inputs.Count;
        return null;
    }

    /// <inheritdoc/>
    protected override INodeOutput? GetOutputInternal(ref int index)
    {
        var @base = base.GetOutputInternal(ref index);
        if (@base != null) return @base;
        if (index >= 0 && index < Outputs.Count) return Outputs[index];
        index -= Outputs.Count;
        return null;
    }

    /// <inheritdoc/>
    protected override ISyncRef? GetGlobalRefInternal(ref int index)
    {
        var @base = base.GetGlobalRefInternal(ref index);
        if (@base != null) return @base;
        if (index == 0) return Function;
        index--;
        return null;
    }

    public string? FunctionName { get; private set; }

    public readonly NodeInputsStruct Inputs;
    public readonly NodeOutputsStruct Outputs;
    public readonly SyncRef<FunctionExportGlobal> Function;
}