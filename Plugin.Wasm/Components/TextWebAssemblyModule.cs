using System.Threading;
using FrooxEngine;
using Wasmtime;

namespace Plugin.Wasm.Components;

/// <summary>
/// Provides a statically loaded WebAssembly module asset.
/// </summary>
[Category(["Web Assembly", "Assets"])]
public sealed class TextWebAssemblyModule() : DynamicAssetProvider<WebAssemblyModule>()
{
    private SpinLock updateLock = new(false);

    private CancellationTokenSource? _cancelUpdate;

    public readonly Sync<string?> Text;

    private string? _error;
    private bool _errorDirty;
    private bool _textDirty;
    public readonly RawOutput<string?> Error;

    /// <inheritdoc/>
    protected override void OnChanges()
    {
        if (_errorDirty)
        {
            _errorDirty = false;
            Error.Value = _error;
        }
        base.OnChanges();
    }

    protected override void SyncMemberChanged(IChangeable member)
    {
        if (member == Error) return;
        if (member == Text) _textDirty = true;
        base.SyncMemberChanged(member);
    }

    /// <inheritdoc/>
    protected override void UpdateAsset(WebAssemblyModule asset)
    {
        if (!_textDirty) return;
        _textDirty = false;
        _cancelUpdate?.Cancel();
        _cancelUpdate = null;
        asset.RequestWriteLock(this, WriteLockGranted);
    }

    private void WriteLockGranted(IAsset asset)
    {
        if (IsDisposed)
        {
            asset.ReleaseWriteLock(this);
            return;
        }
        var wasm = (WebAssemblyModule)asset!;
        string? text = Text;
        if (text is null)
        {
            wasm.Unload();
            wasm.ReleaseWriteLock(this);
            return;
        }

        _cancelUpdate = new();
        var token = _cancelUpdate.Token;
        Engine.WorkProcessor.Enqueue(() => UpdateAssetBackground(wasm, text, token));
    }

    private void UpdateAssetBackground(WebAssemblyModule asset, string text, CancellationToken cancellation)
    {
        try
        {
            if (cancellation.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            var engine = WasmEngineProvider.Engine;
            Wasmtime.Module newModule;
            try
            {
                newModule = Wasmtime.Module.FromText(engine, $"{ReferenceID}.wat", text);
            }
            catch (WasmtimeException error)
            {
                SetErrorThreadSafe(error.Message);
                return;
            }
            if (IsDisposed)
            {
                newModule.Dispose();
                return;
            }
            asset.ReplaceModule(newModule);
            SetErrorThreadSafe(null);
        }
        finally
        {
            asset.ReleaseWriteLock(this);
        }
    }

    private void SetErrorThreadSafe(string? error)
    {
        bool acquired = false;
        try
        {
            this.updateLock.Enter(ref acquired);
            this._errorDirty = _error != error;
            this._error = error;
        }
        finally
        {
            if (acquired) updateLock.Exit();
        }
        MarkChangeDirty();
    }

    /// <inheritdoc/>
    protected override void AssetCreated(WebAssemblyModule asset) { }

    /// <inheritdoc/>
    protected override void ClearAsset() { }
}
