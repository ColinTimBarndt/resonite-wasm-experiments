using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using Elements.Core;

namespace Plugin.Wasm;

public readonly struct FunctionSignature(Type[] parameters, Type[] results) : IEquatable<FunctionSignature>
{
    private readonly Type[] _parameters = parameters;
    private readonly Type[] _results = results;

    /// <summary>
    /// The parameter types of the function.
    /// </summary>
    public IReadOnlyList<Type> Parameters => _parameters.AsReadOnly();

    /// <summary>
    /// The result types of the function.
    /// </summary>
    public IReadOnlyList<Type> Results => _results.AsReadOnly();

    public bool HasParameters => _parameters.Length != 0;
    public bool HasResults => _results.Length != 0;

    private Type[] GetGenericArgs(out Type? resultType)
    {
        int arity = _parameters.Length;
        if (_results.Length != 0) arity++;
        var genArgs = new Type[arity];
        int i = 0;
        for (; i < _parameters.Length; i++)
        {
            genArgs[i] = _parameters[i];
        }
        if (_results.Length != 0)
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
            genArgs[_parameters.Length] = resultType;
        }
        else
        {
            resultType = null;
        }
        return genArgs;
    }

    public Type GetDelegateType(out Type? resultType)
    {
        Type? delegateType = null;
        bool hasResults = _results.Length != 0;
        int arity = _parameters.Length;
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

    public Delegate? TryCreateDelegate(Wasmtime.Function func)
    {
        if (_parameters.Length == 0 && _results.Length == 0) return func.WrapAction();
        var genArgs = GetGenericArgs(out _);
        var methodName = _results.Length == 0 ? "WrapAction" : "WrapFunc";

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
        if (_parameters.Length != other._parameters.Length
            || _results.Length != other._results.Length) return false;
        for (int i = 0; i < _parameters.Length; i++)
        {
            var a = _parameters[i];
            var b = other._parameters[i];
            if (a != b) return false;
        }
        for (int i = 0; i < _results.Length; i++)
        {
            var a = _results[i];
            var b = other._results[i];
            if (a != b) return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var param in _parameters) hash.Add(param);
        foreach (var param in _results) hash.Add(param);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var str = new StringBuilder();
        str.Append('(');
        if (!HasParameters) str.Append("void");
        else str.AppendJoin(' ', Parameters.Select(b => b.Name));
        str.Append(" -> ");
        if (!HasParameters) str.Append("void");
        else str.AppendJoin(' ', Results.Select(b => b.Name));
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
