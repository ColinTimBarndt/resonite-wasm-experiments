using System;

namespace Plugin.Wasm.GenericCollections;

public class CustomStructEditorAttribute(Type component) : Attribute
{
    public readonly Type StructEditorComponent = component;
}
