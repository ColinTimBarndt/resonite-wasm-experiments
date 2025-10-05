using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Utility;

namespace Plugin.Wasm;

/// <summary>
/// The signature of a WebAssembly function.
/// It consists of the runtime types of all parameters and results.
/// </summary>
public readonly struct FunctionSignature(Type[]? parameters, Type[]? results) : IEquatable<FunctionSignature>
{
    /// <summary>The empty function signature (void -> void).</summary>
    public static readonly FunctionSignature EMPTY = new(null, null);

    // Invariant: array is not null => array.Length != 0
    private readonly Type[]? _parameters = parameters?.Length == 0 ? null : parameters;
    private readonly Type[]? _results = results?.Length == 0 ? null : results;

    /// <summary>
    /// The parameter types of the function.
    /// </summary>
    public IReadOnlyList<Type> Parameters => _parameters?.AsReadOnly() ?? ReadOnlyCollection<Type>.Empty;

    /// <summary>
    /// The result types of the function.
    /// </summary>
    public IReadOnlyList<Type> Results => _results?.AsReadOnly() ?? ReadOnlyCollection<Type>.Empty;

    /// <summary>If this function takes any parameters.</summary>
    public bool HasParameters => _parameters is not null;

    /// <summary>If this funtion returns any results.</summary>
    public bool HasResults => _results is not null;

    private Type[] GetGenericArgs(out Type? resultType)
    {
        int arity = Parameters.Count;
        if (HasResults) arity++;
        var genArgs = new Type[arity];
        if (_parameters is not null)
        {
            Array.Copy(_parameters, genArgs, _parameters.Length);
        }
        if (_results is not null)
        {
            if (_results.Length > 1)
            {
                Type? tuple = null;
                var resultCardinality = _results.Length - 1;
                if (resultCardinality < TupleTypes.Length) tuple = TupleTypes[resultCardinality];
                if (tuple is null)
                {
                    throw new NotImplementedException("Arbitrary result cardinality");
                }
                tuple = tuple.MakeGenericType(_results);
                resultType = tuple;
            }
            else
            {
                resultType = _results[0];
            }
            genArgs[^1] = resultType;
        }
        else
        {
            resultType = null;
        }
        return genArgs;
    }

    /// <summary>
    /// Get the type that the callable <see langword="delegate"/> would have.
    /// Either Action or Func.
    /// </summary>
    /// <seealso cref="System.Action"/>
    /// <seealso cref="System.Action{T}"/>
    /// <seealso cref="System.Func{TResult}"/>
    /// <seealso cref="System.Func{T, TResult}"/>
    public Type GetDelegateType(out Type? resultType)
    {
        Type? delegateType = null;
        bool hasResults = HasResults;
        int arity = Parameters.Count;
        if (hasResults)
        {
            // FuncTypes array starts at arity 1
            if (arity < FuncTypes.Length) delegateType = FuncTypes[arity];
            arity++;
        }
        else if (arity < ActionTypes.Length)
        {
            delegateType = ActionTypes[arity];
        }

        if (delegateType is null)
        {
            throw new NotImplementedException("Arbitrary arity");
        }

        var genArgs = GetGenericArgs(out resultType);
        return arity == 0 ? delegateType : delegateType.MakeGenericType(genArgs);
    }

    /// <summary>
    /// Attempts to create a <see langword="delegate"/> with this signature for <paramref name="func"/>.
    /// </summary>
    public Delegate? TryCreateDelegate(Wasmtime.Function func)
    {
        if (!HasParameters && !HasResults) return func.WrapAction();
        var genArgs = GetGenericArgs(out _);
        var methodName = HasResults ? "WrapFunc" : "WrapAction";

        UniLog.Log($"TryCreateDelegate({func}) with {methodName}<{string.Join<Type>(',', genArgs)}>");
        var method = typeof(Wasmtime.Function).GetMethod(methodName, genArgs.Length, BindingFlags.Instance | BindingFlags.Public, null, [], null)
            ?? throw new Exception("Unable to find method to create delegate with");

        return (Delegate?)method.MakeGenericMethod(genArgs).Invoke(func, null);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FunctionSignature other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(FunctionSignature other)
    {
        if (_parameters?.Length != other._parameters?.Length
            || _results?.Length != other._results?.Length) return false;

        if (_parameters != other.Parameters && _parameters is not null && other._parameters is not null)
        {
            for (int i = 0; i < _parameters.Length; i++)
            {
                var a = _parameters[i];
                var b = other._parameters[i];
                if (a != b) return false;
            }
        }
        if (_results != other._results && _results is not null && other._results is not null)
        {
            for (int i = 0; i < _results.Length; i++)
            {
                var a = _results[i];
                var b = other._results[i];
                if (a != b) return false;
            }
        }
        return true;
    }

    /// <summary>Tests if the signatures are the same.</summary>
    public static bool operator ==(FunctionSignature left, FunctionSignature right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests if the signatures are not the same.</summary>
    public static bool operator !=(FunctionSignature left, FunctionSignature right)
    {
        return !(left == right);
    }

    /// <summary>Gets the function signature of a WebAssembly function.</summary>
    public FunctionSignature(in Wasmtime.Function function) : this(
        function.Parameters.Count == 0 ? null : MapTypes(function.Parameters),
        function.Results.Count == 0 ? null : MapTypes(function.Results)
    )
    { }

    private static Type[] MapTypes(IReadOnlyList<Wasmtime.ValueKind> values)
    {
        var types = new Type[values.Count];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = values[i] switch
            {
                Wasmtime.ValueKind.Int32 => typeof(int),
                Wasmtime.ValueKind.Int64 => typeof(long),
                Wasmtime.ValueKind.Float32 => typeof(float),
                Wasmtime.ValueKind.Float64 => typeof(double),
                Wasmtime.ValueKind.V128 => typeof(Wasmtime.V128),
                Wasmtime.ValueKind.FuncRef => typeof(object), // TODO
                Wasmtime.ValueKind.ExternRef => typeof(object),
                Wasmtime.ValueKind.AnyRef => typeof(object),
                _ => throw new NotImplementedException(),
            };
        }
        return types;
    }

    /// <summary>
    /// Saves this function signature to a data tree node.
    /// </summary>
    public DataTreeNode Save(SaveControl control)
    {
        var dict = new DataTreeDictionary();
        if (_parameters is not null) dict.Add("Parameters", SaveTypes(control, _parameters));
        if (_results is not null) dict.Add("Results", SaveTypes(control, _results));
        return dict;
    }

    private static DataTreeList SaveTypes(SaveControl control, Type[] types)
    {
        var list = new DataTreeList();
        foreach (var type in types)
        {
            list.Add(control.SaveType(type));
        }
        return list;
    }

    /// <summary>
    /// Loads a function signature from a data tree node.
    /// </summary>
    public static FunctionSignature Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dict) return EMPTY;
        return new(
            LoadTypes(dict.TryGetList("Parameters"), control),
            LoadTypes(dict.TryGetList("Results"), control)
        );
    }

    private static Type[]? LoadTypes(DataTreeList? list, LoadControl control)
    {
        if (list is null || list.Count == 0) return null;
        var types = new Type[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            types[i] = control.DecodeType(list[i]).type;
        }
        return types;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (_parameters is not null)
            foreach (var param in _parameters) hash.Add(param);
        if (_results is not null)
            foreach (var param in _results) hash.Add(param);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var str = new StringBuilder();
        str.Append('(');
        if (!HasParameters) str.Append("void");
        else str.AppendJoin(' ', Parameters.Select(b => b.GetNiceName()));
        str.Append(" -> ");
        if (!HasResults) str.Append("void");
        else str.AppendJoin(' ', Results.Select(b => b.GetNiceName()));
        str.Append(')');
        return str.ToString();
    }

    private static readonly Type[] ActionTypes = [
        typeof(Action), typeof(Action<>), typeof(Action<,>), typeof(Action<,,>),
        typeof(Action<,,,>), typeof(Action<,,,,>), typeof(Action<,,,,,>), typeof(Action<,,,,,,>),
        typeof(Action<,,,,,,,>), typeof(Action<,,,,,,,,>), typeof(Action<,,,,,,,,,>), typeof(Action<,,,,,,,,,,>),
        typeof(Action<,,,,,,,,,,,>), typeof(Action<,,,,,,,,,,,,>), typeof(Action<,,,,,,,,,,,,,>), typeof(Action<,,,,,,,,,,,,,,>),
        typeof(Action<,,,,,,,,,,,,,,,>)
    ];

    private static readonly Type[] FuncTypes = [
        typeof(Func<>), typeof(Func<,>), typeof(Func<,,>), typeof(Func<,,,>),
        typeof(Func<,,,,>), typeof(Func<,,,,,>), typeof(Func<,,,,,,>), typeof(Func<,,,,,,,>),
        typeof(Func<,,,,,,,,>), typeof(Func<,,,,,,,,,>), typeof(Func<,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,>),
        typeof(Func<,,,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,,,,,>),
        typeof(Func<,,,,,,,,,,,,,,,,>),
    ];

    private static readonly Type[] TupleTypes = [
        typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>), typeof(Tuple<,,,,>),
        typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>), typeof(Tuple<,,,,,,,>),
    ];

    internal static readonly ConstructorInfo Constructor = typeof(FunctionSignature).GetConstructor([typeof(Type[]), typeof(Type[])])
        ?? throw new Exception("Constructor not found");
}
