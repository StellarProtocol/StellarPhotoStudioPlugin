using System;
using System.Collections;
using Panda.ZGame;
using Panda.ZInput;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 2 / E — input mask under a NON-EPhoto source. <c>ZIgnoreMgr.SetInputIgnore</c> has an <c>int source</c>
/// overload (and <c>IsIgnoreExcludeSource(…, ulong sourceMask)</c> ⇒ sources are bit positions of a 64-bit mask), so
/// the probe tries source 28 (= <c>EIgnoreMaskSource.Count</c>, the first value past the enum) and 40 (well past it),
/// one at a time on the Move bit, each released immediately; then a coexistence check (EPhoto + own source held, own
/// released ⇒ EPhoto still holds). While a mask is held it reads the Rewired keyboard controller's enabled flag
/// (framework hotkeys poll <c>UnityEngine.Input.GetKey</c> — see the recon doc — so they sit upstream of this mask).
/// </summary>
public sealed partial class FreeCamProbe
{
    private IEnumerator StepInputSource2()
    {
        var epoch = _sceneEpoch;
        var ig = ZIgnoreMgr.IsCreated ? ZIgnoreMgr.Instance : null;
        var pic = PlayerInputController.IsCreated ? PlayerInputController.Instance : null;
        if (ig == null || pic == null) { Log("E prerequisites missing"); yield break; }
        var moveBit = 1UL << (int)EInputMask.Move;
        Log($"E before: ignore={IgnoreBits(ig)} moveType={pic.MoveInputType} rewiredKeyboard={RewiredKeyboard()}");
        foreach (var src in new[] { 28, 40, (int)EIgnoreMaskSource.EGm })
        {
            if (Aborted(epoch)) yield break;
            var key = $"input2.src{src}";
            var ok = false;
            try
            {
                ig.SetInputIgnore(moveBit, true, src);
                ok = true;
                Arm(key, () => ZIgnoreMgr.Instance?.SetInputIgnore(moveBit, false, src));
            }
            catch (Exception ex) { Log($"E source {src}: SetInputIgnore(int) THREW {ex.GetType().Name}: {Short(ex.Message)}"); }
            if (!ok) continue;
            try
            {
                yield return Frames(3);
                Log($"E source {src}: accepted; IsInputIgnore(Move)={ig.IsInputIgnore(EInputMask.Move)} IsIgnoreBySource(InputMask,Move,{src})={BySource(ig, src)} " +
                    $"IsInputIgnoreBySource(Move,(E){src})={BySourceEnum(ig, src)} moveType={pic.MoveInputType} rewiredKeyboard={RewiredKeyboard()} ignore={IgnoreBits(ig)}");
            }
            finally { Release(key); }
            yield return Frames(2);
            Log($"E source {src}: released; IsInputIgnore(Move)={ig.IsInputIgnore(EInputMask.Move)} moveType={pic.MoveInputType} ignore={IgnoreBits(ig)}");
        }

        // Coexistence: EPhoto + own source; releasing ours must leave EPhoto's hold intact.
        if (Aborted(epoch)) yield break;
        const int own = (int)EIgnoreMaskSource.EGm;   // run 2: 28 (=Count) throws IndexOutOfRange, 40 is silently ignored ⇒ only enum values work
        try
        {
            ig.SetInputIgnore(moveBit, true, EIgnoreMaskSource.EPhoto);
            Arm("input2.photo", () => ZIgnoreMgr.Instance?.SetInputIgnore(moveBit, false, EIgnoreMaskSource.EPhoto));
            ig.SetInputIgnore(moveBit, true, own);
            Arm("input2.own", () => ZIgnoreMgr.Instance?.SetInputIgnore(moveBit, false, own));
            yield return Frames(2);
            Release("input2.own");
            yield return Frames(2);
            Log($"E coexist: after releasing own({own}) with EPhoto held: IsInputIgnore(Move)={ig.IsInputIgnore(EInputMask.Move)} bySource(EPhoto)={ig.IsInputIgnoreBySource(EInputMask.Move, EIgnoreMaskSource.EPhoto)} bySource({own})={BySource(ig, own)}");
        }
        finally { Release("input2.own"); Release("input2.photo"); }
        yield return Frames(2);
        Log($"E after all: ignore={IgnoreBits(ig)} moveType={pic.MoveInputType} rewiredKeyboard={RewiredKeyboard()}");
    }

    private static string BySource(ZIgnoreMgr ig, int src)
    {
        try { return ig.IsIgnoreBySource(EIgnoreType.InputMask, (int)EInputMask.Move, src).ToString(); }
        catch (Exception ex) { return "ERR:" + ex.GetType().Name; }
    }

    private static string BySourceEnum(ZIgnoreMgr ig, int src)
    {
        try { return ig.IsInputIgnoreBySource(EInputMask.Move, (EIgnoreMaskSource)src).ToString(); }
        catch (Exception ex) { return "ERR:" + ex.GetType().Name; }
    }

    private static string RewiredKeyboard()
    {
        try { return Rewired.ReInput.isReady ? Rewired.ReInput.controllers.Keyboard.enabled.ToString() : "not-ready"; }
        catch (Exception ex) { return "ERR:" + ex.GetType().Name; }
    }
}
