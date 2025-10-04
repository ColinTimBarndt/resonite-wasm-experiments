using System;
using System.Reflection;
using System.Reflection.Emit;
using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal static class Reflection
{
    public static readonly Type NodeType = typeof(Node);
    public static readonly Type IExecutionNodeType = typeof(IExecutionNode<>);

    private static readonly ConstructorInfo NodeNameAttributeCtor;

    private static readonly Type ValueInputType = typeof(ValueInput<>);
    private static readonly Type ValueArgumentType = typeof(ValueArgument<>);

    private static readonly Type ObjectInputType = typeof(ObjectInput<>);
    private static readonly Type ObjectArgumentType = typeof(ObjectArgument<>);

    private static readonly Type ValueOutputType = typeof(ValueOutput<>);
    private static readonly Type ObjectOutputType = typeof(ObjectOutput<>);

    private static readonly Type ExecutionContextExtensionsType = typeof(ExecutionContextExtensions);
    private static readonly MethodInfo
        EvaluateValueMethod, EvaluateObjectMethod,
        ReadValueMethod, ReadObjectMethod,
        WriteValueMethod, WriteObjectMethod;

    static Reflection()
    {
        NodeNameAttributeCtor = typeof(NodeNameAttribute).GetConstructor([typeof(string), typeof(bool)])
            ?? throw new Exception("NodeNameAttribute constructor not found");

        const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Static;
        var T0 = Type.MakeGenericMethodParameter(0);
        var intType = typeof(int);
        var ctx = typeof(ExecutionContext);

        // Inputs
        {
            var valInput = ValueInputType.MakeGenericType(T0);
            var objInput = ObjectInputType.MakeGenericType(T0);

            var evalVal = ExecutionContextExtensionsType.GetMethod("Evaluate", 1, FLAGS, [valInput, ctx, T0]);
            EvaluateValueMethod = evalVal ?? throw new Exception("Cannot find value evaluate method");

            var evalObj = ExecutionContextExtensionsType.GetMethod("Evaluate", 1, FLAGS, [objInput, ctx, T0]);
            EvaluateObjectMethod = evalObj ?? throw new Exception("Cannot find object evaluate method");
        }

        // Arguments
        {
            var readVal = ExecutionContextExtensionsType.GetMethod("ReadValue", 1, FLAGS, [intType, ctx]);
            ReadValueMethod = readVal ?? throw new Exception("Cannot find value read method");

            var readObj = ExecutionContextExtensionsType.GetMethod("ReadObject", 1, FLAGS, [intType, ctx]);
            ReadObjectMethod = readObj ?? throw new Exception("Cannot find object read method");
        }

        // Outputs
        {
            var valOutput = ValueOutputType.MakeGenericType(T0);
            var objOutput = ObjectOutputType.MakeGenericType(T0);

            var writeVal = ExecutionContextExtensionsType.GetMethod("Write", 1, FLAGS, [valOutput, T0, ctx]);
            WriteValueMethod = writeVal ?? throw new Exception("Cannot find value write method");

            var writeObj = ExecutionContextExtensionsType.GetMethod("Write", 1, FLAGS, [objOutput, T0, ctx]);
            WriteObjectMethod = writeObj ?? throw new Exception("Cannot find object write method");
        }
    }

    public static void SetNodeName(this TypeBuilder type, string name, bool simpleView = false)
    {
        type.SetCustomAttribute(new CustomAttributeBuilder(NodeNameAttributeCtor, [name, simpleView]));
    }

    /// <summary>
    /// Gets the (static) method to evaluate a ProtoFlux Node input of the given type.
    /// </summary>
    public static MethodInfo GetInputEvaluationMethod(Type type)
    {
        var baseMethod = (type.IsValueType ? EvaluateValueMethod : EvaluateObjectMethod);
        return baseMethod.MakeGenericMethod(type);
    }

    /// <summary>
    /// Gets the (static) method to read a ProtoFlux Node argument of the given type.
    /// </summary>
    public static MethodInfo GetArgumentReadMethod(Type type)
    {
        var baseMethod = (type.IsValueType ? ReadValueMethod : ReadObjectMethod);
        return baseMethod.MakeGenericMethod(type);
    }

    /// <summary>
    /// Gets the (static) method to write a ProtoFlux Node output of the given type.
    /// </summary>
    public static MethodInfo GetOutputWriteMethod(Type type)
    {
        var baseMethod = (type.IsValueType ? WriteValueMethod : WriteObjectMethod);
        return baseMethod.MakeGenericMethod(type);
    }

    public static Type GetNodeInputType(Type type)
    {
        var wrapper = type.IsValueType ? ValueInputType : ObjectInputType;
        return wrapper.MakeGenericType(type);
    }

    public static Type GetNodeArgumentType(Type type)
    {
        var wrapper = type.IsValueType ? ValueArgumentType : ObjectArgumentType;
        return wrapper.MakeGenericType(type);
    }

    public static Type GetNodeOutputType(Type type)
    {
        var wrapper = type.IsValueType ? ValueOutputType : ObjectOutputType;
        return wrapper.MakeGenericType(type);
    }

    public static ConstructorInfo GetNodeOutputConstructor(NodeOutput output)
    {
        return output.Field.FieldType.GetConstructor([Reflection.NodeType])
            ?? throw new Exception("Constructor not found");
    }
}
