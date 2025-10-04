using System;
using System.Reflection;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

internal enum PropertyMethod
{
    NONE,
    Get,
    Set,
}

internal interface IPropertyCompiler<S> : IMethodCompiler<S> where S : INodeStateBuilder
{
    PropertyMethod MethodType { get; }

    PropertyInfo GetProperty(Type type, S state);
}
