using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal sealed class InternalSetDelegateMethodCompiler : BaseMethodOverrideCompiler<DelegateState>
{
    public override string MethodName => "InternalSetDelegate";

    public override MethodAttributes MethodAttributes => base.MethodAttributes | MethodAttributes.Family;
    protected override BindingFlags BindingFlags => base.BindingFlags | BindingFlags.NonPublic;

    public override Type[] GetParameterTypes(Type ctx) => [typeof(Delegate)];
    public override Type? ReturnType => null;

    public override void Build(ILGenerator il, NodeBuilder<DelegateState> node)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, node.State.DelegateType);
        il.Emit(OpCodes.Stfld, node.State.DelegateField!);
        il.Emit(OpCodes.Ret);
    }
}

internal sealed class SignatureGetterCompiler : BaseMethodOverrideCompiler<DelegateState>, IPropertyCompiler<DelegateState>
{
    public override string MethodName => throw new NotImplementedException();

    public override Type? ReturnType => typeof(FunctionSignature);

    public PropertyMethod MethodType => PropertyMethod.Get;

    public override MethodAttributes MethodAttributes => base.MethodAttributes | MethodAttributes.Public;

    public override void Build(ILGenerator il, NodeBuilder<DelegateState> node)
    {
        il.Emit(OpCodes.Ldsfld, node.State.SignatureField!);
        il.Emit(OpCodes.Ret);
    }

    public override Type[] GetParameterTypes(Type executionContext) => [];

    public PropertyInfo GetProperty(Type type, DelegateState state)
        => type.BaseType!.GetProperty("Signature", BindingFlags.Public | BindingFlags.Instance)
        ?? throw new Exception("Signature property not found");
}
