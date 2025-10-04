using System;
using System.Reflection;
using System.Reflection.Emit;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

/// <summary>
/// Builds the 'main' method of a ProtoFlux node, which varies depending on the base type of node.
/// </summary>
internal interface IRunMethodCompiler<S> : IMethodOverrideCompiler<S> where S : INodeStateBuilder
{
    bool CanBeEvaluated { get; }
}

internal abstract class BaseRunMethodCompiler<S> : BaseMethodOverrideCompiler<S>, IRunMethodCompiler<S> where S : INodeStateBuilder
{
    public abstract bool CanBeEvaluated { get; }

    public sealed override MethodAttributes MethodAttributes => base.MethodAttributes | MethodAttributes.Family;
    protected sealed override BindingFlags BindingFlags => base.BindingFlags | BindingFlags.NonPublic;

    public override Type? ReturnType => null;
    public override Type[] GetParameterTypes(Type ctx) => [ctx];

    protected static void EmitPushInputs(ILGenerator il, NodeBuilder<S> node)
    {
        foreach (NodeInput input in node.NodeInputs)
        {
            il.EmitEvalNodeInput(input);
        }
    }

    protected static void EmitPushArguments(ILGenerator il, NodeBuilder<S> node)
    {
        foreach (NodeInput input in node.NodeInputs)
        {
            il.EmitReadNodeArgument(input);
        }
    }
}
