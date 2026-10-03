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
        _launcherEntry = _services.Launcher.Register(new LauncherEntry(
            _loc.T("ps.title"), LoadIconPng(), IconKey: null, OnOpen: () => _panel.Set(true))
        // Re-localize the tile title live on a language change (Title alone is a captured string).
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
