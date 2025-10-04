using System;
using System.Collections.Generic;
using System.Reflection;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal readonly struct NodeInput(FieldInfo field, Type type, int index)
{
    public readonly FieldInfo Field = field;
    public readonly Type Type = type;
    public readonly int Index = index;
}

internal readonly struct NodeOutput(FieldInfo field, Type type)
{
    public readonly FieldInfo Field = field;
    public readonly Type Type = type;
}

internal partial class NodeBuilder<S> where S : INodeStateBuilder
{
    private readonly List<NodeInput> nodeInputs = [];
    private int valueInputs = 0;
    private int objectInputs = 0;

    public IReadOnlyList<NodeInput> NodeInputs => nodeInputs.AsReadOnly();

    private readonly List<NodeOutput> nodeOutputs = [];

    public IReadOnlyList<NodeOutput> NodeOutputs => nodeOutputs.AsReadOnly();

    public void DefineNodeInput(Type inputType, string? name = null)
    {
        const FieldAttributes ATTRIBUTES = FieldAttributes.Public;

        var fieldType = CanBeEvaluated ? Reflection.GetNodeArgumentType(inputType) : Reflection.GetNodeInputType(inputType);
        var field = type.DefineField(name ?? $"Arg{nodeInputs.Count + 1}", fieldType, ATTRIBUTES);

        int index = inputType.IsValueType ? valueInputs++ : objectInputs++;

        NodeInput entry = new(field, inputType, index);
        this.nodeInputs.Add(entry);
    }

    public void DefineNodeOutput(Type outputType, string? name = null)
    {
        const FieldAttributes ATTRIBUTES = FieldAttributes.Public | FieldAttributes.InitOnly;

        var fieldType = Reflection.GetNodeOutputType(outputType);
        var field = type.DefineField(name ?? $"Res{nodeOutputs.Count + 1}", fieldType, ATTRIBUTES);

        NodeOutput entry = new(field, outputType);
        this.nodeOutputs.Add(entry);
    }
}