using System;
using ProtoFlux.Core;
using Elements.Core;
using FrooxEngine.ProtoFlux;
using Plugin.Wasm.GenericCollections;
using ProtoFlux.Runtimes.Execution;
using Plugin.Wasm.Components;
using CallFunctionNode = Plugin.Wasm.ProtoFlux.CallFunction;
using FrooxEngine.UIX;
using Plugin.Wasm;
using Plugin.Wasm.ProtoFlux;

namespace FrooxEngine.Plugin.Wasm.ProtoFlux
{
    [Category("ProtoFlux/Runtimes/Execution/Nodes/Web Assembly")]
    public partial class CallFunction : global::FrooxEngine.ProtoFlux.Runtimes.Execution.ActionBreakableFlowNode<ExecutionContext>
    {
        private FunctionSignature _functionSignature = new([], []);

        public override Type NodeType => CallFunctionJit.GetSpecializedType(_functionSignature);

        public CallFunctionNode? TypedNodeInstance { get; private set; }
        public override INode? NodeInstance => TypedNodeInstance;

        public override N Instantiate<N>()
        {
            UniLog.Log("Instantiate", true);

            if (TypedNodeInstance != null)
                throw new InvalidOperationException("Node has already been instantiated");

            var node = CallFunctionJit.NewSpecialized(_functionSignature);
            node.TrySetFunction(Function.Target?.Function);

            UniLog.Log($"Instance: {node}");

            TypedNodeInstance = node;

            OnInstantiated();

            return (node as N)!;
        }

        partial void OnInstantiated();

        public override void BuildContentUI(ProtoFluxNodeVisual visual, UIBuilder ui)
        {
            UniLog.Log($"CallFunction::BuildContentUI");
            base.BuildContentUI(visual, ui);
            //SyncMemberEditorBuilder.Build(Function, "Function", typeof(CallFunction).GetField("Function")!, ui);
            ui.Empty();
            ui.Nest();
            var slot = ui.Empty();
            var rect = ui.CurrentRect;
            rect.OffsetMin.Value = new float2(16f, 0f);
            rect.OffsetMax.Value = new float2(-16f, 0f);
            slot.AttachComponent<Button>();
            var receiver = slot.AttachComponent<ReferenceReceiver<WebAssemblyInstance.FunctionExport>>();
            receiver.Reference.Target = Function;
            ui.NestOut();
        }

        protected override void AssociateInstanceInternal(INode node)
        {
            UniLog.Log($"AssociateInstanceInternal {node}", true);
            if (node is CallFunctionNode typedNode)
            {
                TypedNodeInstance = typedNode;
                _functionSignature = typedNode.Signature;
                if (!typedNode.TrySetFunction(Function.Target?.Function))
                {
                    typedNode.TrySetFunction(null);
                }
                EnsureTypedLayout();

                OnInstantiated();
            }
            else
                throw new ArgumentException("Node instance is not of type " + typeof(CallFunctionNode));
        }

        private void EnsureTypedLayout()
        {
            var meta = NodeMetadataHelper.GetMetadata(NodeType);
            NodeInputs.EnsureTypedLayout(meta);
            NodeOutputs.EnsureTypedLayout(meta);
        }

        protected override void OnAttach()
        {
            UniLog.Log("CallFunction OnAttach");
            EnsureTypedLayout();
            base.OnAttach();
        }

        protected override void SyncMemberChanged(IChangeable member)
        {
            base.SyncMemberChanged(member);
            if (member != Function) return;

            UniLog.Log("CallFunction Function Changed");
            var func = Function.Target;
            functionName = func?.Name.Value;
            if (
                (TypedNodeInstance is not null && TypedNodeInstance.TrySetFunction(func?.Function))
                || func is null)
            {
                UpdateVisual();
                return;
            }
            var sig = func.Signature;
            UniLog.Log($"Mark for rebuild; {sig}");
            _functionSignature = func.Signature;
            ClearInstance();
            Instantiate<CallFunctionNode>();
            EnsureTypedLayout();
            UpdateVisual();
            World.ProtoFlux.RegisterDirtyNode(this);
            World.ProtoFlux.ScheduleGroupRebuild(this.Group);
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
                        ProtoFluxVisualHelper.EnsureVisual(this);
                });
            }
        }

        public override void ClearInstance() => TypedNodeInstance = null;

        public override int NodeInputCount => base.NodeInputCount + NodeInputs.Count;

        public override int NodeOutputCount => base.NodeOutputCount + NodeOutputs.Count;

        protected override ISyncRef? GetInputInternal(ref int index)
        {
            var @base = base.GetInputInternal(ref index);

            if (@base != null)
                return @base;

            if (index >= 0 && index < NodeInputs.Count) return NodeInputs[index];

            index -= NodeInputs.Count;
            return null;
        }

        protected override INodeOutput? GetOutputInternal(ref int index)
        {
            var @base = base.GetOutputInternal(ref index);

            if (@base != null)
                return @base;

            if (index >= 0 && index < NodeOutputs.Count) return NodeOutputs[index];

            index -= NodeOutputs.Count;
            return null;
        }

        private string? functionName;

        /// <inheritdoc/>
        public override string NodeName => functionName ?? "Call Function";

        public readonly SyncRef<WebAssemblyInstance.FunctionExport> Function;
        public readonly NodeInputsStruct NodeInputs;
        public readonly NodeOutputsStruct NodeOutputs;
    }
}
