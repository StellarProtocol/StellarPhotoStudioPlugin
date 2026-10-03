using System;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

// Posing self-test MEASUREMENT (review fix round 1 (5); env-gated with the rest of the posing self-test): what the game's
// action readback does at and after the end of an emote, so the controller can decide the end-of-action gate (not
// added yet). Before the detect steps: a ONE-SHOT emote, then a LOOPING one, both played through the game's own path,
// each read at 4 Hz through IPosing.TryGetCurrentAction:
//   [PhotoStudio] posing selftest probe=oneshot|loop t=<s> id=<id> moment=<0-1> detected=<bool>
// The raw passed / total (unclamped, id 0 included) are on the framework's paired diagnostics line logged by that same
// read (STELLAR_DIAGNOSTICS=1, which the scenario sets): [Posing] action-raw uuid=… id=… passed=… total=…
// Looping = EmoteTable.EmoteType 2 (the framework's EmoteInfo.Looping: the expression VM's show-data list type 2);
// one-shot = EmoteType 1 (e.g. 9011 Wave). Dance I (9020, EmoteType 2, 4.80 s in recon run 4) is the preferred loop.
public sealed partial class Plugin
{
    private const float PzProbeRate = 0.25f, PzProbeTail = 3f, PzProbeOneShotCap = 20f, PzProbeLoopSpan = 15f;
    private bool _pzProbeLoop;
    private float _pzProbeStart, _pzProbeEndAt;

    private void PzProbeStart(bool loop)
    {
        _pzProbeLoop = loop;
        var e = PzProbeEmote(loop, out var why);
        var result = _services.Emotes.PlayAsync(e.Id).Result;
        _services.Log.Info($"[PhotoStudio] posing selftest probe={PzProbeName} start id={e.Id} name={e.Name} looping={e.Looping} result={result} why={why}");
        _pzProbeStart = _pzClock;
        _pzProbeEndAt = loop ? PzProbeLoopSpan : -1f;
        PzDue(PzProbeRate);
    }

    /// <summary>One 4 Hz read + line; true once the probe is over (one-shot: 3 s after it reached its end or stopped
    /// being reported, at most 20 s; loop: 15 s — ≥ 2 cycles of Dance I).</summary>
    private bool PzProbeTick()
    {
        var t = _pzClock - _pzProbeStart;
        var detected = _services.Posing.TryGetCurrentAction(_services.CombatSnapshot.LocalEntityId, out var id, out var moment);
        _services.Log.Info($"[PhotoStudio] posing selftest probe={PzProbeName} t={t:F2} id={id} moment={moment:F3} detected={detected}");
        if (!_pzProbeLoop && _pzProbeEndAt < 0f && (!detected || moment >= 0.999f)) _pzProbeEndAt = t + PzProbeTail;
        var end = _pzProbeLoop ? _pzProbeEndAt : _pzProbeEndAt < 0f ? PzProbeOneShotCap : Math.Min(_pzProbeEndAt, PzProbeOneShotCap);
        if (t >= end) return true;
        PzDue(PzProbeRate);
        return false;
    }

    private string PzProbeName => _pzProbeLoop ? "loop" : "oneshot";

    private EmoteInfo PzProbeEmote(bool loop, out string why)
    {
        var unlocked = _services.Emotes.Unlocked;
        var preferred = loop ? 9020 : 9011;
        foreach (var e in unlocked)
            if (e.Id == preferred && e.Looping == loop) { why = $"preferred id, EmoteTable.EmoteType={(loop ? 2 : 1)}"; return e; }
        foreach (var e in unlocked)
            if (e.Looping == loop) { why = $"first unlocked with EmoteTable.EmoteType={(loop ? 2 : 1)} (EmoteInfo.Looping={loop})"; return e; }
        why = "none unlocked of that kind — table fallback";
        return loop ? new EmoteInfo(9020, "Dance I", "", true) : new EmoteInfo(9011, "Wave", "", false);
    }
}
