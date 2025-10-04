using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Elements.Core;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal interface INodeBuilder
{
    bool CanBeEvaluated { get; }

    Type Build();
}

internal partial class NodeBuilder<S> : INodeBuilder where S : INodeStateBuilder
{
    private readonly TypeBuilder type;

    public Type ContextType { get; private init; }

    private readonly IRunMethodCompiler<S> runMethodCompiler;

    public S State { get; private init; }

    /// <summary>
    /// If this node can be evaluted, then all inputs are arguments.
    /// </summary>
    public bool CanBeEvaluated => runMethodCompiler.CanBeEvaluated;

    private bool isBuilt = false;

    private NodeBuilder(TypeBuilder type, Type context, S state, IRunMethodCompiler<S> runCompiler)
    {
        this.type = type;
        runMethodCompiler = runCompiler;
        State = state;
        ContextType = context;
        AddCompiler(runCompiler);
    }

    private readonly List<IMethodCompiler<S>> methodCompilers = [];

    public void AddCompiler(IMethodCompiler<S> compiler)
    {
        methodCompilers.Add(compiler);
    }

    public static NodeBuilder<S> Create(ModuleBuilder module, string name, Type parent, S state, IRunMethodCompiler<S> runCompiler)
    {
        const TypeAttributes NODE_CLASS_ATTRIBUTES = TypeAttributes.Class | TypeAttributes.Public
                    | TypeAttributes.Sealed | TypeAttributes.AutoLayout | TypeAttributes.AnsiClass;

        var type = module.DefineType(name, NODE_CLASS_ATTRIBUTES, parent);

        Type? contextType = null;
        foreach (var intf in parent.EnumerateInterfacesRecursively())
        {
            if (intf.IsGenericType && intf.GetGenericTypeDefinition().Equals(Reflection.IExecutionNodeType))
            {
                contextType = intf.GenericTypeArguments[0];
                break;
            }
        }

        if (contextType is null) throw new ArgumentException("Not an IExecutionNode<C>", nameof(parent));

        type.SetNodeName(name);

        // FIELDS
        state.DefineFields(type);

        return new(type, contextType, state, runCompiler);
    }
}
