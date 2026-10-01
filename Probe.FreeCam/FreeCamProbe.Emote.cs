using System.Collections;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 4 — emotes through the game's own VM (driving-game-actions.md Approach B). Enumerates the loaded VMs
/// (<c>debug.getupvalue(Z.VMMgr.GetVM, i)</c> → the <c>v2vm</c> registry) whose names mention emote / action /
/// expression and their function names, then plays ONE harmless action through the emote wheel's own path:
/// <c>Z.VMMgr.GetVM("expression").PlayAction(9011, true, false)</c> (EmoteTable 9011 "Wave", Type 1 one-shot; dot-style
/// call; sync, so pcall is fine) → <c>Z.ZAnimActionPlayMgr:PlayAction</c>. Gates logged first: CheckEmoteCondition,
/// GetEmoteIsVisable, CanPlayActionCheck(local actor state). No packet is built here.
/// </summary>
public sealed partial class FreeCamProbe
{
    private const int EmoteId = 9011;   // "Wave" — Condition [[133,1,...]] like every basic player action (tutorial-gated)

    private IEnumerator StepEmote()
    {
        var epoch = _sceneEpoch;
        Log("EMOTE VM registry: " + Lua(
            "local reg; for i=1,40 do local n,v = debug.getupvalue(Z.VMMgr.GetVM, i); if n == nil then break end; " +
            "if type(v)=='table' and n=='v2vm' then reg=v end end; if not reg then return 'v2vm upvalue not found' end; " +
            "local names, hits = {}, {}; for k,_ in pairs(reg) do names[#names+1]=tostring(k); local s=string.lower(tostring(k)); " +
            "if s:find('emote') or s:find('action') or s:find('express') then hits[#hits+1]=tostring(k) end end; " +
            "table.sort(hits); return #names .. ' loaded; matches=' .. table.concat(hits, ',')"));
        foreach (var vm in new[] { "expression", "action", "multaction", "camerasys" })
            Log($"EMOTE VM '{vm}' functions: " + Lua(
                $"local vm = Z.VMMgr.GetVM('{vm}'); if not vm then return 'nil' end; local f = {{}}; " +
                "for k,v in pairs(vm) do if type(v)=='function' then f[#f+1]=tostring(k) end end; table.sort(f); return #f .. ': ' .. table.concat(f, ' ')"));
        Log("EMOTE gates: " + Lua(
            "local function s(f) local ok, r = pcall(f); return ok and tostring(r) or ('ERR:' .. tostring(r)) end; " +
            $"local vm = Z.VMMgr.GetVM('expression'); local id = {EmoteId}; " +
            "local row = Z.TableMgr.GetTable('EmoteTableMgr').GetRow(id); local st = Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState(); " +
            "return 'row=' .. tostring(row ~= nil) .. ' name=' .. s(function() return row.Name end) .. ' type=' .. s(function() return row.Type end) .. " +
            "' visible=' .. s(function() return vm.GetEmoteIsVisable(row) end) .. ' condition=' .. s(function() return vm.CheckEmoteCondition(id, false) end) .. " +
            "' actorState=' .. tostring(st) .. ' canPlayInState=' .. s(function() return vm.CanPlayActionCheck(st) end) .. " +
            "' statusSwitch=' .. s(function() return Z.StatusSwitchMgr:CheckSwitchEnable(Z.EStatusSwitch.ActorStateAction) end)"));
        Log("EMOTE refusal gate samples (CheckEmoteCondition, showTips=false): " + Lua(
            "local vm = Z.VMMgr.GetVM('expression'); local out = {}; for _,id in ipairs({9001, 9011, 1002, 10039, 999999}) do " +
            "out[#out+1] = id .. '=' .. tostring(vm.CheckEmoteCondition(id, false)) end; return table.concat(out, ' ')"));

        var region = SelfRegion();
        var before = region is { } r0 ? Capture("4_emote_before", r0) : null;
        Log($"EMOTE play {EmoteId} via expression.PlayAction(id, true, false): " + Lua(
            $"Z.VMMgr.GetVM('expression').PlayAction({EmoteId}, true, false); return 'returned; actorState now=' .. tostring(Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState())"));
        for (var i = 0; i < 4 && !Aborted(epoch); i++)
        {
            yield return Wait(0.5f);
            var shot = region is { } r ? Capture($"4_emote_t{i}", r) : null;
            Log($"EMOTE t+{(i + 1) * 0.5f:F1}s actorState=" + Lua("return tostring(Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState())") + " " + DiffText(before, shot));
        }
        yield return Wait(2f);
        Log("EMOTE t+4s actorState=" + Lua("return tostring(Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState())"));
    }
}
