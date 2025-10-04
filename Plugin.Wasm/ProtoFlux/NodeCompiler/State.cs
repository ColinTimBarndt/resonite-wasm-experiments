using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Plugin.Wasm.ProtoFlux.NodeCompiler;

/// <summary>
/// Builds a node's state fields.
/// </summary>
internal interface INodeStateBuilder
{
    IEnumerable<FieldInfo> Fields { get; }

    void DefineFields(TypeBuilder type);

    void InitializeFields(ILGenerator il) { }

    void InitializeStaticFields(ILGenerator il) { }
}

/// <summary>
/// Builds no state.
/// </summary>
internal sealed class VoidState : INodeStateBuilder
{
    public IEnumerable<FieldInfo> Fields => [];

    public void DefineFields(TypeBuilder type) { /* no fields */ }
}
