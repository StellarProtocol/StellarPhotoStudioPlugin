using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
namespace Stellar.PhotoStudio;

/// <summary>Owns the plugin's single look handle; pushes at most one update per tick. The active
/// target depends on whether the panel/game photo mode is open (draft, always PlayMode=false),
/// pinned (kept applied with PlayMode=true once closed), or suspended (removed entirely, e.g.
/// while a cutscene plays).</summary>
internal sealed class LookController : IDisposable
{
    private readonly IRenderLook _look;
    private ILookHandle? _handle;
    private LookSettings? _draft;
    private bool _panelOpen, _gamePhoto, _pinned, _suspended, _dirty;

    public LookController(IRenderLook look) => _look = look;

    public LookSettings? Draft => _draft;
    public void SetDraft(LookSettings s) { _draft = s; _dirty = true; }
    public void SetPanelOpen(bool v) { _panelOpen = v; _dirty = true; }
    public void SetGamePhotoActive(bool v) { _gamePhoto = v; _dirty = true; }
    public void SetPinned(bool v) { _pinned = v; _dirty = true; }
    public void SetSuspended(bool v) { _suspended = v; _dirty = true; }

    public void Tick()
    {
        if (!_dirty) return;
        _dirty = false;
        var target = Target();
        if (target is null) { Release(); return; }
        if (_handle is { IsActive: true }) _handle.Update(target);
        else _handle = _look.Apply(target);
    }

    public void Dispose() => Release();

    private LookSettings? Target()
    {
        if (_draft is null || _suspended) return null;
        if (_panelOpen || _gamePhoto) return _draft with { PlayMode = false };
        return _pinned ? _draft with { PlayMode = true } : null;
    }

    private void Release()
    {
        _handle?.Dispose();
        _handle = null;
    }
}
