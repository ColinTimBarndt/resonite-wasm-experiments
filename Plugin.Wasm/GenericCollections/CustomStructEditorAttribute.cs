using System;

namespace Plugin.Wasm.GenericCollections;

/// <summary>
/// Tells the worker inspector UI generator to use the specified
/// struct editor component instead of the default.
/// </summary>
/// <seealso cref="Plugin.Wasm.Components.StructEditor"/>
[AttributeUsage(AttributeTargets.Field)]
public class CustomStructEditorAttribute(Type component) : Attribute
{
    /// <summary>
    /// The struct editor to use instead of the default.
    /// </summary>
    public readonly Type StructEditorComponent = component;
}
