using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;

namespace KamiLib.Extensions;

public static class ConditionExtensions {
    public static bool IsBoundByDuty(this ICondition condition) 
        => condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95);

    public static bool IsInCombat(this ICondition condition)
        => condition.Any(ConditionFlag.InCombat);

    public static bool IsInCutscene(this ICondition condition)
        => condition.Any(ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78);

    public static bool IsBetweenAreas(this ICondition condition)
        => condition.Any(ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51);

    public static bool IsCrafting(this ICondition condition)
        => condition.Any(ConditionFlag.Crafting, ConditionFlag.ExecutingCraftingAction, ConditionFlag.PreparingToCraft);

    public static bool IsCrossWorld(this ICondition condition)
        => condition.Any(ConditionFlag.ParticipatingInCrossWorldPartyOrAlliance);

    public static bool IsGathering(this ICondition condition)
        => condition.Any(ConditionFlag.Gathering, ConditionFlag.ExecutingGatheringAction);

    public static bool IsInBardPerformance(this ICondition condition)
        => condition.Any(ConditionFlag.Performing);

    public static unsafe bool IsIslandDoingSomethingMode() {
        // ⚠️ MJIManager 宣告為 [StaticAddress(..., isPointer: true)],產生的 Instance() 只在
        // 「特徵碼解析不到」時擲例外,回傳的卻是 *ppInstance 本身——遊戲還沒配置管理器時
        // (登入前、角色選擇)會靜默回 null,讀 CurrentMode(0x10) 與 IsPlayerInSanctuary(0x06) 即崩潰。
        // 另原本一行呼叫 Instance() 兩次,這裡一併改成取一次。
        var manager = MJIManager.Instance();
        if (manager is null) return false;

        return manager->CurrentMode is not 0 && manager->IsPlayerInSanctuary;
    }

    public static bool IsInQuestEvent(this ICondition condition)
        => condition.Any(ConditionFlag.OccupiedInQuestEvent) || IsIslandDoingSomethingMode();

    public static bool IsInCutsceneOrQuestEvent(this ICondition condition)
        => condition.IsInCutscene() || condition.IsInQuestEvent();

    public static bool IsDutyRecorderPlayback(this ICondition condition)
        => condition.Any(ConditionFlag.DutyRecorderPlayback);
}