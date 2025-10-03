using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Elements.Core;
using FrooxEngine;

namespace Plugin.Wasm.GenericCollections;

public delegate void SyncStructElementsEvent(SyncElementStruct @struct, int startIndex, int count);

public abstract class SyncElementStruct : ConflictingSyncElement
{
    public struct Enumerator(SyncElementStruct _syncStruct) : IEnumerator<ISyncMember>
    {
        private List<NodeRecord>.Enumerator structEnumerator;

        public ISyncMember Current => structEnumerator.Current.node;

        object IEnumerator.Current => Current;

        public void Dispose() => structEnumerator.Dispose();

        public bool MoveNext() => structEnumerator.MoveNext();

        public void Reset()
        {
            IEnumerator temp = structEnumerator;
            temp.Reset();
            structEnumerator = (List<NodeRecord>.Enumerator)temp;
        }
    }

    private readonly struct SyncStructEnumerableWrapper(SyncElementStruct @struct) : IEnumerable<ISyncMember>
    {
        private readonly SyncElementStruct _struct = @struct;

        public Enumerator GetEnumerator() => _struct.GetElementsEnumerator();

        IEnumerator<ISyncMember> IEnumerable<ISyncMember>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private class NodeRecord(ISyncMember _node, Type _type)
    {
        public Type type = _type;
        public ISyncMember node = _node;
        public bool isDirty;
        public int deltaRecordIndex;
    }

    private enum DeltaMessage : byte
    {
        // bits: id[5], index[4], ordinal[3..0]
        Empty = 0,
        Add = 0b10_0001,
        Insert = 0b11_0010,
        Remove = 0b01_0011,
        Clear = 0b00_0100,

        HAS_INDEX = 0b01_0000,
        HAS_ID = 0b10_0000,
    }

    private readonly struct DeltaRecord(NodeRecord? _record, int _index, RefID _id, Type? _type, DeltaMessage _message)
    {
        public readonly DeltaMessage message = _message;
        public readonly int index = _index;
        public readonly RefID id = _id;
        public readonly Type? type = _type;
        public readonly NodeRecord? record = _record;

        public bool IsAddition => HasId(message);

        private static bool HasId(DeltaMessage msg) => ((byte)(msg & DeltaMessage.HAS_ID)) != 0;
        private static bool HasIndex(DeltaMessage msg) => ((byte)(msg & DeltaMessage.HAS_INDEX)) != 0;

        public static DeltaRecord Add(NodeRecord record) => new(record, -1, record.node.ReferenceID, record.type, DeltaMessage.Add);
        public static DeltaRecord Insert(NodeRecord record, int index) => new(record, index, record.node.ReferenceID, record.type, DeltaMessage.Insert);
        public static DeltaRecord Remove(int index) => new(null, index, 0, null, DeltaMessage.Insert);
        public static DeltaRecord Clear() => new(null, -1, 0, null, DeltaMessage.Clear);
        public static DeltaRecord Empty() => new(null, -1, 0, null, DeltaMessage.Empty);

        public DeltaRecord ShiftIndex(int delta)
        {
            //IL_000f: Unknown result type (might be due to invalid IL or missing references)
            return new DeltaRecord(record, index + delta, id, type, message);
        }

        public void Encode(BinaryWriter writer, RefID offset, TypeManager types)
        {
            writer.Write((byte)message);
            if (HasId(message))
            {
                types.EncodeType(writer, type);
                writer.Write7BitEncoded((ulong)(id - offset));
            }
            if (HasIndex(message)) writer.Write7BitEncoded((ulong)index);
        }

        public static DeltaRecord Decode(BinaryReader reader, RefID offset, TypeManager types)
        {
            var message = (DeltaMessage)reader.Read7BitEncoded();
            RefID id = 0;
            Type? type = null;
            int index = -1;
            if (HasId(message))
            {
                type = types.DecodeType(reader);
                id = reader.Read7BitEncoded() + offset;
            }
            if (HasIndex(message)) index = (int)reader.Read7BitEncoded();
            return new(null, index, id, type, message);
        }
    }

    private List<NodeRecord>? records = [];
    private List<DeltaRecord>? _deltaRecords;

    /// <summary>
    /// Creates a new member instance for the given type.
    /// </summary>
    protected abstract ISyncMember NewMember(Type type);

    /// <inheritdoc/>
    public sealed override IEnumerable<ILinkable> LinkableChildren => records!.ConvertAll(r => r.node);

    protected Enumerator GetElementsEnumerator() => new(this);

    public IEnumerable<ISyncMember> Elements => new SyncStructEnumerableWrapper(this);

    public int Count => records!.Count;

    public int IndexOfElement(ISyncMember element) => records!.FindIndex(r => r.node == element);

    public ISyncMember GetElement(int index) => records![index].node;

    public ISyncMember this[int index] => GetElement(index);

    public ISyncMember Insert(int index, Type type) => InternalInsert(RefID.Null, type, index);

    public ISyncMember Add(Type type) => InternalInsert(RefID.Null, type, records!.Count);

    public ISyncMember AddElement(Type type) => Add(type);

    public void Clear() => InternalClear();

    public void RemoveAt(int index) => InternalRemove(index);

    public void RemoveElement(int index) => RemoveAt(index);

    public bool RemoveElement(ISyncMember member)
    {
        int index = IndexOfElement(member);
        if (index == -1) return false;
        RemoveAt(index);
        return true;
    }

    public ISyncMember MoveToIndex(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Count)
        {
            throw new ArgumentOutOfRangeException("oldIndex");
        }
        if (newIndex < 0 || newIndex >= Count)
        {
            throw new ArgumentOutOfRangeException("newIndex");
        }
        var type = records![oldIndex].type;
        if (oldIndex == newIndex)
        {
            return this[oldIndex];
        }
        if (newIndex < oldIndex)
        {
            oldIndex++;
        }
        if (oldIndex < newIndex)
        {
            newIndex++;
        }
        var val = Insert(newIndex, type);
        var element = GetElement(oldIndex);
        val.CopyValues(element);
        Dictionary<IWorldElement, IWorldElement> dictionary = new Dictionary<IWorldElement, IWorldElement>();
        dictionary.Add(element, val);
        if (element is Worker worker && val is Worker worker2)
        {
            List<IWorldElement> syncMembers = worker.GetSyncMembers<IWorldElement>();
            List<IWorldElement> syncMembers2 = worker2.GetSyncMembers<IWorldElement>();
            if (syncMembers.Count == syncMembers2.Count)
            {
                for (int i = 0; i < syncMembers.Count; i++)
                {
                    IWorldElement worldElement = syncMembers[i];
                    if (worldElement != element)
                    {
                        dictionary.Add(worldElement, syncMembers2[i]);
                    }
                }
            }
            else
            {
                UniLog.Warning($"Number of source elements on the old struct ({syncMembers.Count}) item doesn't match the new {syncMembers2.Count}", true);
            }
        }
        World.ReplaceReferenceTargets(dictionary, nullIfIncompatible: true);
        RemoveAt(oldIndex);
        return val;
    }

    public ISyncMember MoveElementToIndex(int oldIndex, int newIndex) => MoveToIndex(oldIndex, newIndex);

    public void EnsureTypedLayout(IEnumerable<Type> types)
    {
        int i = 0;
        foreach (var expectedType in types)
        {
            if (i >= Count)
            {
                Add(expectedType);
                i++;
                continue;
            }
            var r = records![i++];
            if (r.type == expectedType) continue;

            RemoveAt(i);
            Insert(i, expectedType);
        }
        while (Count > i)
        {
            RemoveAt(Count - 1);
        }
    }

    public event SyncStructElementsEvent? ElementsAdded;
    public event SyncStructElementsEvent? ElementsRemoving;
    public event SyncStructElementsEvent? ElementsRemoved;

    private void SendElementsAdded(int index, int count = 1)
    {
        try
        {
            ElementsAdded?.Invoke(this, index, count);
        }
        catch (Exception ex)
        {
            UniLog.Error($"Exception running ElementsAdded. On Element:\n{this.ParentHierarchyToString()}\nException:\n{ex}", true);
        }
    }

    private void SendElementsRemoving(int index, int count = 1)
    {
        try
        {
            ElementsRemoving?.Invoke(this, index, count);
        }
        catch (Exception ex)
        {
            UniLog.Error($"Exception running ElementsRemoving. On Element:\n{this.ParentHierarchyToString()}\nException:\n{ex}", true);
        }
    }

    private void SendElementsRemoved(int index, int count = 1)
    {
        try
        {
            ElementsRemoved?.Invoke(this, index, count);
        }
        catch (Exception ex)
        {
            UniLog.Error($"Exception running ElementsRemoved. On Element:\n{this.ParentHierarchyToString()}\nException:\n{ex}", true);
        }
    }

    private List<DeltaRecord> GetDeltaRecords()
    {
        _deltaRecords ??= Pool.BorrowList<DeltaRecord>();
        return _deltaRecords;
    }

    private void InternalClear(bool sync = true, bool change = true, bool forceTrash = false)
    {
        BeginModification();
        if (records!.Count == 0)
        {
            EndModification();
            return;
        }
        int count = records.Count;
        if (change)
        {
            BlockModification();
            SendElementsRemoving(0, count);
            UnblockModification();
        }
        foreach (NodeRecord record in records)
        {
            if (sync || forceTrash)
            {
                World.MoveToTrash(record.node);
            }
            else
            {
                record.node.Dispose();
            }
        }
        records.Clear();
        if (sync && base.GenerateSyncData)
        {
            List<DeltaRecord> deltaRecords = GetDeltaRecords();
            deltaRecords.Clear();
            deltaRecords.Add(DeltaRecord.Clear());
            InvalidateSyncElement();
        }
        if (change)
        {
            BlockModification();
            SyncElementChanged();
            SendElementsRemoved(0, count);
            UnblockModification();
        }
        EndModification();
    }

    private ISyncMember InternalInsert(RefID id, Type type, int index, bool sync = true, bool change = true)
    {
        BeginModification();
        if (id != RefID.Null)
        {
            base.World.ReferenceController.AllocationBlockBegin(in id);
        }
        else if (base.IsLocalElement)
        {
            base.World.ReferenceController.LocalAllocationBlockBegin();
        }
        var element = NewMember(type);
        element.Initialize(World, this);
        if (id != RefID.Null)
        {
            base.World.ReferenceController.AllocationBlockEnd();
        }
        else if (base.IsLocalElement)
        {
            base.World.ReferenceController.LocalAllocationBlockEnd();
        }
        InternalInsertNode(element, type, index, sync, change);
        EndModification();
        return element;
    }

    private void InternalInsertNode(ISyncMember element, Type type, int index, bool sync = true, bool change = true)
    {
        BeginModification();
        NodeRecord record = new(element, type);
        if (IsInInitPhase) RegisterNewInitializable(element);
        else
        {
            if (element.IsInInitPhase) element.EndInitPhase();
            if (sync && GenerateSyncData)
            {
                var deltas = GetDeltaRecords();
                record.isDirty = true;
                record.deltaRecordIndex = deltas.Count;
                DeltaRecord item = ((index != records!.Count) ? DeltaRecord.Insert(record, index) : DeltaRecord.Add(record));
                deltas.Add(item);
                InvalidateSyncElement();
            }
        }
        RegisterChildElement(record.node);
        records!.Insert(index, record);
        BlockModification();
        SyncElementChanged();
        if (change) SendElementsAdded(index);
        UnblockModification();
        EndModification();
    }

    private void InternalRemove(int index, bool sync = true, bool change = true)
    {
        if (base.IsInInitPhase)
        {
            throw new Exception("Cannot remove elements during initialization phase!");
        }
        BeginModification();
        if (change)
        {
            BlockModification();
            SendElementsRemoving(index);
            UnblockModification();
        }
        var record = records[index];
        UnregisterChildElement(record.node);
        records.RemoveAt(index);
        bool trash = false;
        if (sync && GenerateSyncData)
        {
            if (record.isDirty)
            {
                var deltas = GetDeltaRecords();
                int num = deltas[record.deltaRecordIndex].index;
                for (int i = record.deltaRecordIndex + 1; i < deltas.Count; i++)
                {
                    var delta = deltas[i];
                    switch (delta.message)
                    {
                        case DeltaMessage.Add:
                        case DeltaMessage.Insert:
                            if (delta.index <= num)
                            {
                                num++;
                            }
                            else
                            {
                                deltas[i] = delta.ShiftIndex(-1);
                            }
                            break;
                        case DeltaMessage.Remove:
                            if (delta.index <= num)
                            {
                                num--;
                            }
                            else
                            {
                                deltas[i] = delta.ShiftIndex(-1);
                            }
                            break;
                    }
                }
                deltas[record.deltaRecordIndex] = DeltaRecord.Empty();
                record.node.Dispose();
            }
            else
            {
                GetDeltaRecords().Add(DeltaRecord.Remove(index));
                trash = true;
            }
            InvalidateSyncElement();
        }
        if (change)
        {
            BlockModification();
            SyncElementChanged();
            SendElementsRemoved(index);
            UnblockModification();
        }
        if (trash)
        {
            base.World.MoveToTrash(record.node);
        }
        else
        {
            record.node.Dispose();
        }
        EndModification();
    }

    /// <inheritdoc/>
    public override string? GetSyncMemberName(ISyncMember member)
    {
        int index = IndexOfElement(member);
        if (index == -1) return null;
        return $"[{index}]";
    }

    /// <inheritdoc/>
    protected sealed override void InternalClearDirty()
    {
        if (_deltaRecords == null)
        {
            return;
        }
        foreach (var record in _deltaRecords)
        {
            if (record.IsAddition) record.record!.isDirty = false;
        }
        Pool.Return<DeltaRecord>(ref _deltaRecords);
        _deltaRecords = null;
    }

    /// <inheritdoc/>
    protected sealed override void InternalCopy(ISyncMember syncSource, Action<ISyncMember, ISyncMember> copy)
    {
        var source = (SyncElementStruct)syncSource;

        Clear();
        if (records!.Capacity < source.Count)
        {
            records.Capacity = source.Count;
        }
        foreach (var r in source.records!)
        {
            copy(r.node, Add(r.type));
        }
    }

    private bool InternalDecodeTypes(
        BinaryReader reader,
        [NotNullWhen(true)] out RawList<Type>? typeList,
        [NotNullWhen(true)] out RawList<ulong>? typeIndices
    )
    {
        int typeCount = reader.Read7BitEncodedInt();
        if (typeCount == 0)
        {
            typeList = null;
            typeIndices = null;
            return false;
        }
        typeList = Pool.BorrowRawList<Type>();
        typeIndices = Pool.BorrowRawList<ulong>();
        try
        {
            var typeMgr = World.Types;
            while (--typeCount >= 0)
            {
                typeList.Add(typeMgr.DecodeType(reader));
            }
            DataCoding.ReadDeltaCodedSequence(reader, typeIndices);
            return true;
        }
        catch (Exception)
        {
            Pool.Return(ref typeList);
            Pool.Return(ref typeIndices);
            throw;
        }
    }

    /// <summary>
    /// Encoding: TYPES LENGTH, [TYPES...], [INDICES...]
    /// </summary>
    private void InternalEncodeTypes(BinaryWriter writer)
    {
        int len = Count;
        if (len == 0)
        {
            writer.Write((byte)0);
            return;
        }
        var typeMap = Pool.BorrowDictionary<Type, int>();
        var typeList = Pool.BorrowList<Type>();
        var typeIndices = Pool.BorrowRawValueList<ulong>();
        try
        {
            foreach (var r in records!)
            {
                if (!typeMap.TryGetValue(r.type, out int typeIndex))
                {
                    typeIndex = typeMap.Count;
                    typeList.Add(r.type);
                }
                typeMap.Add(r.type, typeIndex);
                typeIndices.Add((ulong)typeIndex);
            }
            writer.Write7BitEncodedInt(typeList.Count);
            var typeMgr = World.Types;
            foreach (var type in typeList)
            {
                typeMgr.EncodeType(writer, type);
            }
            DataCoding.WriteDeltaCodedSequence(writer, typeIndices);
        }
        finally
        {
            Pool.Return(ref typeMap);
            Pool.Return(ref typeList);
            Pool.Return(ref typeIndices);
        }
    }

    /// <inheritdoc/>
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        int count = reader.Read7BitEncodedInt();
        RefID offset = reader.Read7BitEncoded();
        var types = World.Types;
        while (--count >= 0)
        {
            var delta = DeltaRecord.Decode(reader, offset, types);
            switch (delta.message)
            {
                case DeltaMessage.Clear:
                    InternalClear(sync: false);
                    break;
                case DeltaMessage.Add:
                    InternalInsert(delta.id, delta.type!, records!.Count, sync: false);
                    break;
                case DeltaMessage.Insert:
                    InternalInsert(delta.id, delta.type!, delta.index, sync: false);
                    break;
                case DeltaMessage.Remove:
                    InternalRemove(delta.index, sync: false);
                    break;
            }
        }
    }

    /// <inheritdoc/>
    protected sealed override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        InternalClear(sync: false, change: true, forceTrash: true);
        if (!InternalDecodeTypes(reader, out var typeList, out var typeIndices))
        {
            return;
        }

        var ids = Pool.BorrowRawValueList<ulong>();

        try
        {
            DataCoding.ReadDeltaCodedSequence(reader, ids);

            ulong tick = base.World.SyncTick;
            if (inboundMessage is ConfirmationMessage confirmationMessage)
            {
                tick = confirmationMessage.ConfirmTime;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                RefID id = ids[i];
                Type type = typeList[(int)typeIndices[i]];
                var syncMember = World.TryRetrieveFromTrash(tick, id);
                if (syncMember is null)
                    InternalInsert(id, type, records!.Count, sync: false, change: false);
                else
                    InternalInsertNode(syncMember, type, records!.Count, sync: false, change: false);
            }

            SendElementsAdded(0, records!.Count);
        }
        finally
        {
            Pool.Return(ref ids);
            Pool.Return(ref typeList);
            Pool.Return(ref typeIndices);
        }
    }

    /// <inheritdoc/>
    protected sealed override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        var deltas = GetDeltaRecords();
        int count = 0;
        var offset = RefID.MaxValue;
        foreach (var delta in deltas)
        {
            if (delta.message != DeltaMessage.Empty) count++;
            if (delta.IsAddition) offset = Math.Min((ulong)offset, (ulong)delta.id);
        }
        writer.Write7BitEncodedInt(count);
        writer.Write7BitEncoded((ulong)offset);
        var types = World.Types;
        foreach (var delta in deltas)
        {
            if (delta.message == DeltaMessage.Empty) continue;
            delta.Encode(writer, offset, types);
        }
    }

    /// <inheritdoc/>
    protected sealed override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        InternalEncodeTypes(writer);
        if (Count == 0) return;
        var ids = Pool.BorrowRawValueList<ulong>();
        foreach (var r in records!)
        {
            ids.Add((ulong)r.node.ReferenceID);
        }
        DataCoding.WriteDeltaCodedSequence(writer, ids);
        Pool.Return(ref ids);
    }

    /// <inheritdoc/>
    protected sealed override void InternalLoad(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeList list) return;
        foreach (DataTreeNode child in list.Children)
        {
            if (child is not DataTreeDictionary dict) continue;
            var type = control.DecodeType(dict["Type"]).type;
            Add(type).Load(dict["Value"], control);
        }
    }

    /// <inheritdoc/>
    protected sealed override DataTreeNode InternalSave(SaveControl control)
    {
        var list = new DataTreeList();
        foreach (var r in records!)
        {
            DataTreeDictionary dict = new();
            dict.Add("Type", control.SaveType(r.type));
            dict.Add("Value", r.node.Save(control));
            list.Add(dict);
        }
        return list;
    }

    /// <inheritdoc/>
    public sealed override void Dispose()
    {
        foreach (var r in records!)
        {
            UnregisterChildElement(r.node);
            r.node.Dispose();
        }
        records.Clear();
        records = null;
        if (_deltaRecords is not null)
        {
            Pool.Return<DeltaRecord>(ref _deltaRecords);
        }
        this.ElementsAdded = null;
        this.ElementsRemoved = null;
        this.ElementsRemoving = null;
        base.Dispose();
    }
}