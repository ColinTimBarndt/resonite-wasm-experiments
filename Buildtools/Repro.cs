using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Repro
{
    internal static void Scan(TypeDefinition type)
    {
        List<FieldDefinition> fields = [];
        List<ElementData> elementTypes = [];

        GetAllFields(type, fields);
        foreach (var field in fields)
        {
            ElementType elementType = ClassifyElement(field);
            if (elementType != 0)
            {
                elementTypes.Add(new()
                {
                    field = field,
                    type = elementType,
                });
            }
        }

        int singleInputCount = 0;
        int singleOutputCount = 0;
        foreach (var element in elementTypes)
        {
            if (element.type == ElementType.Input)
            {
                element.index = singleInputCount++;
            }
            if (element.type == ElementType.Output)
            {
                element.index = singleOutputCount++;
            }
        }
        MethodDefinition defaultCtor = type.Methods.FirstOrDefault(m => m.Name == ".ctor" && !m.HasParameters)
            ?? throw new Exception($"Node {type} has no default constructor");

        foreach (var item in elementTypes)
        {
            Console.WriteLine("Field: {0}", item);
        }

        var methods = type.Methods.ToList();
        foreach (var nestedType in type.NestedTypes)
        {
            foreach (var method2 in nestedType.Methods)
            {
                methods.Add(method2);
            }
        }

        foreach (var method in methods)
        {
            if (!method.HasBody)
            {
                continue;
            }
            Console.WriteLine("Method: {0}", method);
            var il = method.Body.GetILProcessor();
            for (int i = 2; i < il.Body.Instructions.Count; i++)
            {
                Instruction op = il.Body.Instructions[i];
                if (!(op.OpCode == OpCodes.Call))
                {
                    continue;
                }
                object operand = op.Operand;
                if (operand is not GenericInstanceMethod targetMethod) continue;
                var isIndexRepl = IsIndexReplacementMethod(targetMethod);
                Console.WriteLine("IsIndexReplacementMethod({0}) = {1}", targetMethod, isIndexRepl);
                if (isIndexRepl)
                {
                    var op2 = il.Body.Instructions[i - 2];
                    if (op2.OpCode == OpCodes.Ldfld)
                    {
                        Console.WriteLine("Ldfld {0}", op2.Operand);
                    }
                    else
                    {
                        if (!IsInputList(targetMethod)) continue;
                        Console.WriteLine("Replace Input List");
                    }
                    continue;
                }
                if (IsDefaultValueReplacementMethod(targetMethod))
                {
                    var data = FindFieldForCall(il, i, elementTypes);
                    if (data is null) continue;
                    var expectedType = targetMethod.GenericArguments[0];
                    var defaultValue = data.field.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name.StartsWith("DefaultValue"));
                    if (defaultValue != null) continue;
                    // Init default
                    continue;
                }
            }
        }
    }

    public static void GetAllFields(TypeDefinition type, List<FieldDefinition> fields)
    {
        if (type.BaseType != null)
        {
            GetAllFields(type.BaseType.Resolve(), fields);
        }
        fields.AddRange(type.Fields);
    }

    public static ElementType ClassifyElement(FieldDefinition field)
    {
        string typename = field.FieldType.Name;
        if (!field.FieldType.FullName.Contains("ProtoFlux"))
        {
            return ElementType.NONE;
        }
        if (typename.Contains("InputList") || typename.Contains("ArgumentList"))
        {
            return ElementType.InputList;
        }
        if (typename.Contains("ValueInputList") || typename.Contains("ValueArgumentList"))
        {
            return ElementType.InputList;
        }
        if (typename.Contains("ObjectInputList") || typename.Contains("ObjectArgumentList"))
        {
            return ElementType.InputList;
        }
        if (typename.Contains("OutputList"))
        {
            return ElementType.OutputList;
        }
        if (typename.Contains("ValueOutputList"))
        {
            return ElementType.OutputList;
        }
        if (typename.Contains("ObjectOutputList"))
        {
            return ElementType.OutputList;
        }
        if (typename.Contains("CallList") || typename.Contains("ContinuationList"))
        {
            return ElementType.ImpulseList;
        }
        if (typename.Contains("OperationList"))
        {
            return ElementType.OperationList;
        }
        if (typename.StartsWith("GlobalRefList"))
        {
            return ElementType.GlobalRefList;
        }
        if (typename.Contains("ValueInput") || typename.Contains("ValueArgument"))
        {
            return ElementType.Input;
        }
        if (typename.Contains("ObjectInput") || typename.Contains("ObjectArgument"))
        {
            return ElementType.Input;
        }
        if (typename.Contains("ValueOutput"))
        {
            return ElementType.Output;
        }
        if (typename.Contains("ObjectOutput"))
        {
            return ElementType.Output;
        }
        if (typename.Contains("Impulse"))
        {
            return ElementType.Impulse;
        }
        if (typename.Contains("Operation"))
        {
            return ElementType.Operation;
        }
        if (typename.StartsWith("GlobalRef"))
        {
            return ElementType.GlobalRef;
        }
        return ElementType.NONE;
    }

    public static bool IsIndexReplacementMethod(GenericInstanceMethod method)
    {
        if (method.Parameters.Count == 0)
        {
            return false;
        }
        string paramType = method.Parameters[0].ParameterType.Name;
        if (!paramType.Contains("ValueInput") && !paramType.Contains("ValueArgument") && !paramType.Contains("ValueOutput") && !paramType.Contains("ObjectInput") && !paramType.Contains("ObjectArgument") && !paramType.Contains("ObjectOutput") && !paramType.Contains("IInputList"))
        {
            return false;
        }
        if (method.Name.Contains("ReadValue") || method.Name.Contains("ReadObject"))
        {
            return true;
        }
        if (method.Name.Contains("GetInputBuffer"))
        {
            return true;
        }
        if (method.Name.Contains("GetOutputBuffer"))
        {
            return true;
        }
        if (method.Name.Contains("Collect"))
        {
            return true;
        }
        return false;
    }

    public static bool IsDefaultValueReplacementMethod(GenericInstanceMethod method)
    {
        if (method.Parameters.Count == 0)
        {
            return false;
        }
        string paramType = method.Parameters[0].ParameterType.Name;
        if (!paramType.Contains("ValueInput") && !paramType.Contains("ObjectInput") && !paramType.Contains("ValueArgument") && !paramType.Contains("ObjectArgument"))
        {
            return false;
        }
        if (method.Name == "Evaluate")
        {
            return true;
        }
        return false;
    }

    public static bool IsInputList(GenericInstanceMethod method)
    {
        string name = method.Parameters[0].ParameterType.Name;
        if (name.Contains("IInputList"))
        {
            return true;
        }
        if (name.Contains("ValueInputList") || name.Contains("ValueArgumentList"))
        {
            return true;
        }
        if (name.Contains("ObjectInputList") || name.Contains("ObjectArgumentList"))
        {
            return true;
        }
        return false;
    }

    public static ElementData? FindFieldForCall(ILProcessor il, int callIndex, List<ElementData> elementTypes)
    {
        for (int i = callIndex - 1; i >= 0; i--)
        {
            var _op = il.Body.Instructions[i];
            if (!(_op.OpCode != OpCodes.Ldfld))
            {
                var field = (FieldReference)_op.Operand;
                var data = elementTypes.FirstOrDefault(d => d.field.Name == field.Name);
                if (data != null)
                {
                    return data;
                }
            }
        }
        return null;
    }
}

internal enum ElementType
{
    NONE,
    Input,
    Output,
    Impulse,
    Operation,
    GlobalRef,
    InputList,
    OutputList,
    ImpulseList,
    OperationList,
    GlobalRefList
}

internal class FieldData
{
    public int size;

    public int stackOffset;

    public override string ToString()
    {
        return $"size={size}, stackOffset={stackOffset}";
    }
}

internal class ElementData
{
    public FieldDefinition field;

    public ElementType type;

    public int index;

    public override string ToString()
    {
        return $"field={field}, type={type}, index={index}";
    }
}