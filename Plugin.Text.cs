using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// Shared text helpers and the slider fine-step state. The 1.x "?" tip window ("photostudio.tip") and its row widgets
// (HelpDot / HelpToggle / SliderRow / LabeledRow) were replaced in 2.0.0 by the house kit in Style\ (HelpButton + the help
// popover, SettingSlider / SettingToggle / SettingRow).
public sealed partial class Plugin
{
    // The compact strip, the toast and the close confirm (restyled in a later phase) still draw with these.
    // TextMuted (not MenuMuted): MenuMuted is darker in every preset and failed the three dark themes (sandbox re-measure).
    private ColorRgba? Muted() => _services.Theme.Colors.TextMuted;
    private ColorRgba? MenuMuted() => _services.Theme.Colors.MenuMuted;
    // Explicit default text colour: a Color func that flips back to null does not restore the chrome default
    // (measured in-game: a pill kept its warning colour after unpinning), so dynamic colours never return null.
    private ColorRgba? Normal() => _services.Theme.Colors.MenuText;
    private string T(string key) => _loc.T(key);

    /// <summary>One slider row's fine-step state: its ‹ › step, whether its value is being typed.</summary>
    private sealed class FineStep(SliderElement slider, float step, Func<bool>? enabled)
    {
        public bool Editing;
        public bool Can() => enabled?.Invoke() ?? true;
        public void Nudge(int dir) { if (Can()) SetTo(slider.Get() + dir * step); }
        public void SetTo(float v) => slider.Set(Math.Clamp(v, slider.Min, slider.Max));
    }

    /// <summary>The first number in typed text ("1.4", "1,4 m", "f/2.8", "35mm"); null when there is none.</summary>
    internal static float? ParseNumber(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"-?\d+(?:[.,]\d+)?");
        return m.Success && float.TryParse(m.Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>A time of day: "17:30" / "17.5" / "17" → hours.</summary>
    internal static float? ParseHour(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"(\d{1,2})\s*[:h]\s*(\d{1,2})");
        return m.Success ? int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value) / 60f : ParseNumber(text ?? "");
    }

    private static string F(float v, string format) => v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A slider's value text, rebuilt only when the value (or, with <paramref name="langKey"/>, the language)
    /// changes — a drag allocates while it moves, an idle row never.</summary>
    private Func<string> ValueText(Func<float> get, Func<float, string> format, string? langKey = null)
    {
        var cache = new Cached<(float, string)>(k => format(k.Item1));
        return () => cache.Get((get(), langKey == null ? string.Empty : T(langKey)));
    }

    /// <summary>A string composed from live values, rebuilt only when its key changes — so a polled Func hands back the
    /// same instance every refresh and allocates nothing (Plugin-Performance-Rules §2). The key should include the source
    /// catalog string INSTANCE when the text is localized (a language switch swaps it).</summary>
    /// The builder is given once (a delegate made per poll would itself allocate).
    private sealed class Cached<TKey> where TKey : IEquatable<TKey>
    {
        private readonly Func<TKey, string> _build;
        private TKey _key = default!;
        private bool _valid;
        private string _text = string.Empty;

        public Cached(Func<TKey, string> build) => _build = build;

        public string Get(TKey key)
        {
            if (_valid && key.Equals(_key)) return _text;
            _key = key; _valid = true; _text = _build(key);
            return _text;
        }

        public void Reset() => _valid = false;
    }
}
