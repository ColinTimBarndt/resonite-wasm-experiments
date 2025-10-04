using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

/// <summary>
/// Compiles a method.
/// </summary>
internal interface IMethodCompiler<S> where S : INodeStateBuilder
{
    string MethodName { get; }

    MethodAttributes MethodAttributes { get; }

    CallingConventions CallingConventions { get; }

    Type? ReturnType { get; }

    Type[] GetParameterTypes(Type executionContext);

    void Build(ILGenerator il, NodeBuilder<S> node);
}

internal abstract class BaseMethodCompiler<S> : IMethodCompiler<S> where S : INodeStateBuilder
{
    public abstract string MethodName { get; }

    protected virtual bool IsInstance => true;

    const CallingConventions INSTANCE_CONVENTIONS = CallingConventions.Standard | CallingConventions.HasThis;
    const CallingConventions STATIC_CONVENTIONS = CallingConventions.Standard;

    public CallingConventions CallingConventions => IsInstance ? INSTANCE_CONVENTIONS : STATIC_CONVENTIONS;

    public abstract MethodAttributes MethodAttributes { get; }

    public abstract Type? ReturnType { get; }
    public abstract Type[] GetParameterTypes(Type executionContext);

    public abstract void Build(ILGenerator il, NodeBuilder<S> node);
}

/// <summary>
/// Compiles an override method.
/// </summary>
internal interface IMethodOverrideCompiler<S> : IMethodCompiler<S> where S : INodeStateBuilder
{
    MethodInfo? TryGetOverriddenMethod(Type baseType, Type executionContext);
}

internal abstract class BaseMethodOverrideCompiler<S> : BaseMethodCompiler<S>, IMethodOverrideCompiler<S> where S : INodeStateBuilder
{
    public override MethodAttributes MethodAttributes => MethodAttributes.Virtual;

    /// <summary>
    /// Binding flags to use when searching for the overridden method.
    /// </summary>
    protected virtual BindingFlags BindingFlags => IsInstance ? BindingFlags.Instance : BindingFlags.Static;

    public virtual MethodInfo? TryGetOverriddenMethod(Type baseType, Type executionContext)
        => baseType.GetMethod(MethodName, 0, BindingFlags, GetParameterTypes(executionContext));
}
