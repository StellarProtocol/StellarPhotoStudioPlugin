using System;
using System.IO;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>Stellar launcher tile — click to open the Photo Studio panel without the hotkey.</summary>
public sealed partial class Plugin
{
    private IDisposable? _launcherEntry;

    private void RegisterLauncherTile()
    {
        // Title stays the fixed literal "Photo Studio" — the stable pin-identity key (ILauncher.cs:49-50) —
        // so a pinned tile survives a language change; TitleProvider carries the live-localized display.
        _launcherEntry = _services.Launcher.Register(new LauncherEntry(
            "Photo Studio", LoadIconPng(), IconKey: null, OnOpen: () => _panel.Set(true))
        // Re-localize the tile DISPLAY on a language change; Title above never changes (pin identity).
        { TitleProvider = () => _loc.T("ps.title") });
    }

    private void RemoveLauncherTile()
    {
        try { _launcherEntry?.Dispose(); } catch { }
        _launcherEntry = null;
    }

    private static byte[]? LoadIconPng()
    {
        try
        {
            using var s = typeof(Plugin).Assembly.GetManifestResourceStream("Stellar.PhotoStudio.photostudio-icon.png");
            if (s is null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
