using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace KamiLib.Extensions;

public enum DutyType {
	Unknown,
	Savage,
	Ultimate,
	Extreme,
	Unreal,
	Criterion,
	Alliance,
	NormalRaid,
	ChaoticAlliance,
}

public static class DataManagerExtensions {
	// 台服(TC)注意:我們的 Dalamud fork 的 Lumina 會把指定語言的 Excel 請求
	// 靜默改成 client 語言,上游在這裡指定 ClientLanguage.English 實際拿到的
	// 是繁中資料,英文名稱比對("Savage"/"Extreme")永遠不中。
	// 因此改為不指定語言(誠實面對只有繁中表的現實),並在 GetDutyType
	// 加入繁中命名規則判定。
	public static IEnumerable<ContentFinderCondition> GetSavageDuties(this IDataManager dataManager)
		=> dataManager.GetExcelSheet<ContentFinderCondition>()
			.Where(cfc => GetDutyType(cfc) is DutyType.Savage);

	public static IEnumerable<ContentFinderCondition> GetUltimateDuties(this IDataManager dataManager)
		=> dataManager.GetExcelSheet<ContentFinderCondition>()
			.Where(cfc => GetDutyType(cfc) is DutyType.Ultimate);

	public static IEnumerable<ContentFinderCondition> GetExtremeDuties(this IDataManager dataManager)
		=> dataManager.GetExcelSheet<ContentFinderCondition>()
			.Where(cfc => GetDutyType(cfc) is DutyType.Extreme);

	public static IEnumerable<ContentFinderCondition> GetUnrealDuties(this IDataManager dataManager)
		=> dataManager.GetExcelSheet<ContentFinderCondition>()
			.Where(cfc => GetDutyType(cfc) is DutyType.Unreal);

	public static IEnumerable<ContentFinderCondition> GetCriterionDuties(this IDataManager dataManager)
		=> dataManager.GetExcelSheet<ContentFinderCondition>()
			.Where(cfc => GetDutyType(cfc) is DutyType.Criterion);

	public static IEnumerable<ContentFinderCondition> GetAllianceDuties(this IDataManager dataManager)
		=> dataManager.GetExcelSheet<ContentFinderCondition>()
			.Where(cfc => GetDutyType(cfc) is DutyType.Alliance);

	// Warning, expensive operation, as this has to cross-reference multiple data sets.
	public static IEnumerable<ContentFinderCondition> GetLimitedAllianceRaidDuties(this IDataManager dataManager)
		=> dataManager.GetLimitedDuties()
			.Where(cfc => GetDutyType(cfc) is DutyType.Alliance);

	// Warning, expensive operation, as this has to cross-reference multiple data sets.
	public static IEnumerable<ContentFinderCondition> GetLimitedSavageRaidDuties(this IDataManager dataManager)
		=> dataManager.GetLimitedDuties()
			.Where(cfc => GetDutyType(cfc) is DutyType.Savage);

	// Warning, expensive operation, as this has to cross-reference multiple data sets.
	public static IEnumerable<ContentFinderCondition> GetLimitedNormalRaidDuties(this IDataManager dataManager)
		=> dataManager.GetLimitedDuties()
			.Where(cfc => GetDutyType(cfc) is DutyType.NormalRaid);

	private static IEnumerable<ContentFinderCondition> GetLimitedDuties(this IDataManager dataManager)
		=> dataManager.GetExcelSheet<ContentFinderCondition>()
			.Where(cfc => dataManager.GetExcelSheet<InstanceContent>()
				.Where(instanceContent => instanceContent is { WeekRestriction: 1 })
				.Select(instanceContent => instanceContent.RowId)
				.Contains(cfc.Content.RowId));

	public static DutyType GetDutyType(this IDataManager dataManager, ContentFinderCondition cfc)
		=> GetDutyType(dataManager.GetExcelSheet<ContentFinderCondition>().GetRow(cfc.RowId));

	// 名稱判定同時支援英文(上游)與台服繁中命名。
	private static DutyType GetDutyType(ContentFinderCondition cfc) {
		var name = cfc.Name.ExtractText();
		return cfc switch {
			{ ContentType.RowId: 5, ContentMemberType.RowId: 4 } => DutyType.Alliance,
			{ ContentType.RowId: 37 } => DutyType.ChaoticAlliance,
			{ ContentType.RowId: 5 } when name.Contains("Savage") || name.Contains("零式") => DutyType.Savage,
			{ ContentType.RowId: 5 } => DutyType.NormalRaid,
			{ ContentType.RowId: 28 } => DutyType.Ultimate,
			{ ContentType.RowId: 4 } when name.Contains("Extreme") || name.Contains("Minstrel") || IsExtremeTrialNameTC(name) => DutyType.Extreme,
			{ ContentType.RowId: 4 } => DutyType.Unreal,
			{ ContentType.RowId: 30, AllowUndersized: false } => DutyType.Criterion,
			_ => DutyType.Unknown,
		};
	}

	// 台服極神命名(7.20 dump 實查):「極 」/「極王」前綴、
	// 「究極幻想/蒼天幻想」(吟遊詩人的敘事詩)、「終極之戰」(終焉詩人殲滅戰的極神版;普通版是「終結之戰」)。
	// 刻意不用 Contains("極"),避免誤中「究極武器破壞作戰」(普通難度)。
	private static bool IsExtremeTrialNameTC(string name)
		=> name.StartsWith("極 ") || name.StartsWith("極王") ||
		   name.StartsWith("究極幻想") || name.StartsWith("蒼天幻想") ||
		   name == "終極之戰";

	public static unsafe DutyType GetCurrentDutyType(this IDataManager dataManager) {
		var cfc = dataManager.GetExcelSheet<ContentFinderCondition>().GetRow(GameMain.Instance()->CurrentContentFinderConditionId);
		return dataManager.GetDutyType(cfc);
	}
}