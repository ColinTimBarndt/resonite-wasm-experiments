using System;
using Elements.Core;

namespace Plugin.Wasm;

internal static class WasmLinkerProvider
{
    public static Wasmtime.Linker Linker;

    static WasmLinkerProvider()
    {
        Wasmtime.Linker linker = new(WasmEngineProvider.Engine);
        linker.DefineStringModule();
        linker.DefineMathModule();
        Linker = linker;
    }

    private static void DefineStringModule(this Wasmtime.Linker linker)
    {
        const string NS = "string";

        linker.DefineFunction(NS, "len_wtf16", (Func<string?, int>)WasmStrings.LenWtf16);

        linker.DefineFunction(NS, "read_wtf16", (Wasmtime.CallerFunc<string?, int, int, int, int>)WasmStrings.ReadWtf16);

        linker.DefineFunction(NS, "new_wtf16", (Wasmtime.CallerFunc<int, int, string?>)WasmStrings.NewWtf16);
    }

    private static class WasmStrings
    {
        public const int ERROR_NULL = -1;
        public const int ERROR_MEMORY = -2;

        public static int LenWtf16(string? str)
        {
            return str is not null ? str.Length : ERROR_NULL;
        }

        public static int ReadWtf16(Wasmtime.Caller caller, string? str, int readLen, int ptr, int ptrLen)
        {
            if (str is null) return ERROR_NULL;
            if (readLen <= 0) return 0;

            int actualReadLen = int.Min(str.Length, ptrLen);

            var data = (StoreData)caller.Store.GetData()!;
            var memory = data.Memory;
            if (memory is null) return ERROR_MEMORY;

            Span<char> span;
            try
            {
                span = memory.GetSpan<char>(ptr, actualReadLen);
            }
            catch (ArgumentException)
            {
                return ERROR_MEMORY;
            }

            str.AsSpan().CopyTo(span);

            return actualReadLen;
        }

        public static string? NewWtf16(Wasmtime.Caller caller, int ptr, int len)
        {
            var data = (StoreData)caller.Store.GetData()!;
            var memory = data.Memory;
            if (memory is null) return null;

            Span<char> span;
            try
            {
                span = memory.GetSpan<char>(ptr, len);
            }
            catch (ArgumentException)
            {
                return null;
            }

            return string.Create(len, span, CopyChars);
        }

        private static void CopyChars(Span<char> dest, Span<char> src) => src.CopyTo(dest);
    }

    private static void DefineMathModule(this Wasmtime.Linker linker)
    {
        const string NS = "math";

        // trig

        linker.DefineFunction(NS, "sin_f32", (Func<float, float>)MathX.Sin);
        linker.DefineFunction(NS, "sin_f64", (Func<double, double>)MathX.Sin);

        linker.DefineFunction(NS, "cos_f32", (Func<float, float>)MathX.Cos);
        linker.DefineFunction(NS, "cos_f64", (Func<double, double>)MathX.Cos);

        linker.DefineFunction(NS, "tan_f32", (Func<float, float>)MathX.Tan);
        linker.DefineFunction(NS, "tan_f64", (Func<double, double>)MathX.Tan);

        // atrig

        linker.DefineFunction(NS, "asin_f32", (Func<float, float>)MathX.Asin);
        linker.DefineFunction(NS, "asin_f64", (Func<double, double>)MathX.Asin);

        linker.DefineFunction(NS, "acos_f32", (Func<float, float>)MathX.Acos);
        linker.DefineFunction(NS, "acos_f64", (Func<double, double>)MathX.Acos);

        linker.DefineFunction(NS, "atan_f32", (Func<float, float>)MathX.Atan);
        linker.DefineFunction(NS, "atan_f64", (Func<double, double>)MathX.Atan);

        linker.DefineFunction(NS, "atan2_f32", (Func<float, float, float>)MathX.Atan2);
        linker.DefineFunction(NS, "atan2_f64", (Func<double, double, double>)MathX.Atan2);

        // trigh

        linker.DefineFunction(NS, "sinh_f32", (Func<float, float>)MathX.Sinh);
        linker.DefineFunction(NS, "sinh_f64", (Func<double, double>)MathX.Sin);

        linker.DefineFunction(NS, "cosh_f32", (Func<float, float>)MathX.Cosh);
        linker.DefineFunction(NS, "cosh_f64", (Func<double, double>)MathX.Cosh);

        linker.DefineFunction(NS, "tanh_f32", (Func<float, float>)MathX.Tanh);
        linker.DefineFunction(NS, "tanh_f64", (Func<double, double>)MathX.Tanh);

        // other

        linker.DefineFunction(NS, "sqrt_f32", (Func<float, float>)MathX.Sqrt);
        linker.DefineFunction(NS, "sqrt_f64", (Func<double, double>)MathX.Sqrt);

        linker.DefineFunction(NS, "log_f32", (Func<float, float>)MathX.Log);
        linker.DefineFunction(NS, "log_f64", (Func<double, double>)MathX.Log);

        linker.DefineFunction(NS, "log10_f32", (Func<float, float>)MathX.Log10);
        linker.DefineFunction(NS, "log10_f64", (Func<double, double>)MathX.Log10);

        linker.DefineFunction(NS, "exp_f32", (Func<float, float>)MathX.Exp);
        linker.DefineFunction(NS, "exp_f64", (Func<double, double>)MathX.Exp);

        linker.DefineFunction(NS, "pow_f32", (Func<float, float, float>)MathX.Pow);
        linker.DefineFunction(NS, "pow_f64", (Func<double, double, double>)MathX.Pow);
    }
}