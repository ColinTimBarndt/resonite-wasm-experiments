using System;
using System.Linq;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using Plugin.Wasm.GenericCollections;

namespace Plugin.Wasm.Components;

public class StructEditor : Component
{
    protected readonly SyncRef<SyncElementStruct> _targetStruct;

    protected readonly SyncRef<Button> _addNewButton;

    private readonly SyncRef<TextField> _addNewType;

    protected bool setup;

    protected bool reindex;

    private SyncElementStruct? _registeredStruct;

    protected virtual float LabelSize => 0.15f;

    protected virtual FieldInfo? FieldInfo => null;

    protected virtual bool ReorderButtons => true;

    protected virtual bool RemoveButton => true;

    protected virtual int MinLabelWidth => 24;

    public virtual void Setup(SyncElementStruct target, Button button, TextField type)
    {
        _targetStruct.Target = target;
        _addNewButton.Target = button;
        _addNewType.Target = type;
        _addNewButton.Target.Pressed.Target = AddNewPressed;
    }

    /// <inheritdoc/>
    protected override void OnChanges()
    {
        if (World.IsAuthority && _targetStruct.Target != null && !setup)
        {
            setup = true;
            _registeredStruct = _targetStruct.Target;
            base.Slot.DestroyChildren();
            _targetStruct.Target.ElementsAdded += Target_ElementsAdded;
            _targetStruct.Target.ElementsRemoved += Target_ElementsRemoved;
            Target_ElementsAdded(_targetStruct.Target, 0, _targetStruct.Target.Count);
        }
        if (reindex)
        {
            Reindex();
            reindex = false;
        }
    }

    protected virtual void Reindex()
    {
        for (int i = 0; i < Slot.ChildrenCount; i++)
        {
            var childText = Slot[i][0]?.GetComponent<Text>();
            if (childText != null)
            {
                childText.Content.Value = GetElementName(_targetStruct.Target, i);
            }
        }
    }

    [SyncMethod(typeof(Delegate))]
    private void AddNewPressed(IButton button, ButtonEventData eventData)
    {
        if (_targetStruct.Target is ConflictingSyncElement conflicting && conflicting.DirectAccessOnly && !base.LocalUser.IsDirectlyInteracting())
            return;

        var typeText = _addNewType.Target?.Text?.Content;
        if (typeText is null) return;
        var type = NiceTypeParser.TryParse(typeText);
        if (type is null) return;
        _targetStruct.Target.AddElement(type);
    }

    /// <inheritdoc/>
    protected override void OnDispose()
    {
        base.OnDispose();
        if (_registeredStruct is null) return;
        _registeredStruct.ElementsAdded -= Target_ElementsAdded;
        _registeredStruct.ElementsRemoved -= Target_ElementsRemoved;
        _registeredStruct = null;
    }

    private void Target_ElementsRemoved(SyncElementStruct @struct, int startIndex, int count)
    {
        World.RunSynchronously(delegate
        {
            var slots = Pool.BorrowList<Slot>();
            for (int i = startIndex; i < startIndex + count; i++)
            {
                if (i < Slot.ChildrenCount)
                {
                    slots.Add(Slot[i]);
                }
            }
            foreach (Slot item in slots)
            {
                item.Destroy();
            }
            Pool.Return(ref slots);
            for (int j = startIndex; j < Slot.ChildrenCount; j++)
            {
                Slot[j].OrderOffset = j;
            }
            reindex = true;
            MarkChangeDirty();
        });
    }

    private void Target_ElementsAdded(SyncElementStruct @struct, int startIndex, int count)
    {
        var elements = Pool.BorrowList<ISyncMember>();
        for (int i = startIndex; i < startIndex + count; i++)
        {
            elements.Add(@struct.GetElement(i));
        }
        World.RunSynchronously(delegate
        {
            int count = @struct.Count;
            if (count == 0) return;
            for (int num = Slot.ChildrenCount - 1; num >= startIndex; num--)
            {
                Slot[num].OrderOffset += count;
            }
            for (int j = 0; j < elements.Count; j++)
            {
                if (elements[j] is null || elements[j].IsRemoved) continue;

                Slot slot = Slot.AddSlot("Element");
                slot.OrderOffset = startIndex + j;
                BuildListItem(@struct, startIndex + j, elements[j], slot);
            }
            Pool.Return(ref elements);
        });
    }

    [SyncMethod(typeof(Delegate))]
    private void MoveUpPressed(IButton button, ButtonEventData eventData)
    {
        MoveElement(button, eventData, -1);
    }

    [SyncMethod(typeof(Delegate))]
    private void MoveDownPressed(IButton button, ButtonEventData eventData)
    {
        MoveElement(button, eventData, 1);
    }

    private void MoveElement(IButton button, ButtonEventData eventData, int offset)
    {
        if (_targetStruct.Target is ConflictingSyncElement conflicting && conflicting.DirectAccessOnly && !base.LocalUser.IsDirectlyInteracting())
        {
            return;
        }
        int index = FindButtonIndex(button);
        if (index >= 0)
        {
            int newIndex = index + offset;
            if (newIndex >= 0 && !(newIndex >= _targetStruct.Target?.Count))
            {
                _targetStruct.Target?.MoveElementToIndex(index, newIndex);
            }
        }
    }

    [SyncMethod(typeof(Delegate))]
    private void RemovePressed(IButton button, ButtonEventData eventData)
    {
        if (_targetStruct.Target is not ConflictingSyncElement conflicting || !conflicting.DirectAccessOnly || base.LocalUser.IsDirectlyInteracting())
        {
            int index = FindButtonIndex(button);
            if (index >= 0)
            {
                _targetStruct.Target?.RemoveElement(index);
            }
        }
    }

    private int FindButtonIndex(IButton button)
    {
        for (int i = 0; i < Slot.ChildrenCount; i++)
        {
            if (Slot[i].GetComponentsInChildren<Button>().Any(b => b == button))
            {
                return i;
            }
        }
        return -1;
    }

    protected virtual void BuildListItem(SyncElementStruct @struct, int index, ISyncMember listItem, Slot root)
    {
        root.AttachComponent<HorizontalLayout>().Spacing.Value = 4f;
        UIBuilder ui = new(root);
        RadiantUI_Constants.SetupEditorStyle(ui);
        ui.Style.RequireLockInToPress = true;
        ui.Style.MinWidth = -1f;
        ui.Style.FlexibleWidth = 100f;
        BuildListElement(@struct, listItem, GetElementName(@struct, index), ui);
        ui.Style.FlexibleWidth = 0f;
        ui.Style.MinWidth = 24f;
        if (ReorderButtons)
        {
            LocaleString text = "⮝";
            ui.Button(in text).Pressed.Target = MoveUpPressed;
            text = "⮟";
            ui.Button(in text).Pressed.Target = MoveDownPressed;
        }
        if (RemoveButton)
        {
            LocaleString text = "X";
            ui.Button(in text).Pressed.Target = RemovePressed;
        }
    }

    protected virtual void BuildListElement(SyncElementStruct @struct, ISyncMember member, string name, UIBuilder ui)
    {
        SyncMemberEditorBuilder.Build(member, name, FieldInfo!, ui, LabelSize);
    }

    protected virtual string GetElementName(SyncElementStruct @struct, int index) => index.ToString();
}