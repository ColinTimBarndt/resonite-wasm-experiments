using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Elements.Core;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal partial class NodeBuilder<S> : INodeBuilder where S : INodeStateBuilder
{
    const MethodAttributes CTOR_ATTRIBUTES = MethodAttributes.Public
        | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig;

    public Type Build()
    {
        if (isBuilt) throw new InvalidOperationException("Build was already called");
        isBuilt = true;

        // CONSTRUCTOR
        {
            var ctor = type.DefineConstructor(CTOR_ATTRIBUTES, CallingConventions.Standard, Type.EmptyTypes);
            var il = ctor.GetILGenerator();

            var baseCtor = type.BaseType!.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)
                ?? throw new Exception("Base type has no empty constructor");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, baseCtor);

            foreach (var output in nodeOutputs)
            {
                il.EmitInitializeNodeOutput(output);
            }

            State.InitializeFields(il);

            il.Emit(OpCodes.Ret);
        }

        // STATIC CONSTRUCTOR
        {
            var init = type.DefineTypeInitializer();
            var il = init.GetILGenerator();

            State.InitializeStaticFields(il);
        }

        // METHODS
        foreach (var compiler in methodCompilers)
        {
            RunCompiler(compiler);
        }

        return type.CreateType();
    }

    private void RunCompiler(IMethodCompiler<S> compiler)
    {
        const PropertyMethod NO_PROP = PropertyMethod.NONE;

        string name;
        var attributes = compiler.MethodAttributes;

        // Compiles a property method?
        PropertyInfo? property = null;
        PropertyMethod propMethod = NO_PROP;
        if (compiler is IPropertyCompiler<S> propertyCompiler)
        {
            propMethod = propertyCompiler.MethodType;
            if (propMethod is NO_PROP)
                throw new InvalidEnumArgumentException(nameof(PropertyMethod));

            property = propertyCompiler.GetProperty(type, State);
        }

        // Overrides a method?
        MethodInfo? @override = null;
        UniLog.Log($"RUN COMPILER {compiler}, prop={compiler is IPropertyCompiler<S>}, override={compiler is IMethodOverrideCompiler<S>}");
        if (compiler is IMethodOverrideCompiler<S> overrideCompiler)
        {
            @override = propMethod switch
            {
                NO_PROP => overrideCompiler.TryGetOverriddenMethod(type.BaseType!, ContextType),
                PropertyMethod.Get => property!.GetMethod,
                PropertyMethod.Set => property!.SetMethod,
                _ => throw new NotImplementedException(),
            } ?? throw new Exception("Overridden method not found");
            // Use name of original method
            name = @override.Name;
        }
        else
        {
            name = compiler.MethodName;
        }

        // Build the method
        var method = type.DefineMethod(
            name,
            attributes,
            compiler.CallingConventions,
            compiler.ReturnType,
            compiler.GetParameterTypes(ContextType)
        );
        var il = method.GetILGenerator();
        compiler.Build(il, this);

        // Register method if override or property
        if (@override is not null)
        {
            type.DefineMethodOverride(method, @override);
        }
        else if (property is not null)
        {
            if (property is not PropertyBuilder builder)
                throw new Exception("Property must be builder to define getter/setter methods");
            switch (propMethod)
            {
                case PropertyMethod.Get:
                    builder.SetGetMethod(method);
                    break;
                case PropertyMethod.Set:
                    builder.SetSetMethod(method);
                    break;
            }
        }
    }
}