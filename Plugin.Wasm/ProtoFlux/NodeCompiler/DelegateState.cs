using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal sealed class DelegateState : INodeStateBuilder
{
    public DelegateState(FunctionSignature signature)
    {
        DelegateType = signature.GetDelegateType(out var _resultType);
        ResultType = _resultType;
        Signature = signature;
    }

    public Type DelegateType { get; private init; }
    public Type? ResultType { get; private init; }
    public FunctionSignature Signature { get; private init; }

    const BindingFlags INVOKE_FLAGS = BindingFlags.Public | BindingFlags.Instance;
    public MethodInfo InvokeMethod
        => DelegateType.GetMethod("Invoke", INVOKE_FLAGS)
        ?? throw new Exception("Delegate has no Invoke method");

    public FieldInfo? DelegateField { get; private set; }
    public FieldInfo? SignatureField { get; private set; }

    public IEnumerable<FieldInfo> Fields => [DelegateField!, SignatureField!];

    void INodeStateBuilder.DefineFields(TypeBuilder type)
    {
        const FieldAttributes DELEGATE_ATTRIBUTES = FieldAttributes.Private;
        const FieldAttributes SIGNATURE_ATTRIBUTES = FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly;

        DelegateField = type.DefineField("delegate", DelegateType, DELEGATE_ATTRIBUTES);
        SignatureField = type.DefineField("_signature", typeof(FunctionSignature), SIGNATURE_ATTRIBUTES);
    }

    void INodeStateBuilder.InitializeStaticFields(ILGenerator il)
    {
        if (SignatureField is null) throw new NullReferenceException("Fields are not defined");

        il.EmitNewTypeArray(Signature.Parameters);
        il.EmitNewTypeArray(Signature.Results);
        il.Emit(OpCodes.Newobj, FunctionSignature.Constructor);
        il.Emit(OpCodes.Stsfld, SignatureField);
        il.Emit(OpCodes.Ret);
    }
}