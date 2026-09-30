using System;
using System.Threading.Tasks;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
namespace Stellar.PhotoStudio;

internal enum StudioState { Closed, Open, Capturing }

/// <summary>Owns the panel open/closed/capturing state machine, serializing capture requests
/// against the panel's own open/close transitions.</summary>
internal sealed class StudioSession
{
    private readonly IScreenCapture _capture;
    private readonly Func<CaptureRequest> _request;
    private readonly Action<CaptureResult> _onResult;
    private readonly Action<string> _log;
    private bool _closeRequested;
    private bool _wasOpen;

    public StudioSession(IScreenCapture capture, Func<CaptureRequest> request, Action<CaptureResult> onResult, Action<string> log)
    {
        _capture = capture;
        _request = request;
        _onResult = onResult;
        _log = log;
    }

    public StudioState State { get; private set; } = StudioState.Closed;

    public void Open()
    {
        if (State == StudioState.Closed) State = StudioState.Open;
        if (State == StudioState.Capturing) { _wasOpen = true; _closeRequested = false; }
    }

    public void Close()
    {
        if (State == StudioState.Open) State = StudioState.Closed;
        else if (State == StudioState.Capturing) _closeRequested = true;
    }

    public async Task<CaptureResult?> CaptureAsync()
    {
        if (State == StudioState.Capturing || _capture.IsCapturing) return null;
        _wasOpen = State == StudioState.Open;
        _closeRequested = false;
        State = StudioState.Capturing;
        try
        {
            var r = await _capture.CaptureAsync(_request());
            // The caller (e.g. a hotkey handler) must never see an exception from here — a broken
            // toast/UI callback must not also wedge the capture state machine.
            try { _onResult(r); }
            catch (Exception ex) { _log($"[PhotoStudio] capture result handler threw: {ex}"); }
            return r;
        }
        finally
        {
            State = _wasOpen && !_closeRequested ? StudioState.Open : StudioState.Closed;
        }
    }
}
