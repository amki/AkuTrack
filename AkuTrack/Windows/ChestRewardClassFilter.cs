using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AkuTrack.Windows;

internal sealed class ChestRewardClassFilter
{
    internal sealed class ClassFilterOption
    {
        public required uint RowId { get; init; }
        public required string Label { get; init; }
        public string Abbreviation { get; init; } = string.Empty;
    }

    internal sealed class MatchResult
    {
        public HashSet<uint> CompatibleClassJobIds { get; init; } = [];
        public HashSet<uint> DirectClassJobIds { get; init; } = [];
        public HashSet<uint> GroupFilterIds { get; init; } = [];
    }

    public const uint AllClassJobFilter = 0;
    public const uint AllClassesGroupFilter = 1_000_001;
    public const uint TanksGroupFilter = 1_000_002;
    public const uint HealersGroupFilter = 1_000_003;
    public const uint MeleeGroupFilter = 1_000_004;
    public const uint PhysicalRangedGroupFilter = 1_000_005;
    public const uint CastersGroupFilter = 1_000_006;
    public const uint CraftersGroupFilter = 1_000_007;
    public const uint GatherersGroupFilter = 1_000_008;

    private readonly IDataManager dataManager;
    private readonly ClientLanguage clientLanguage;

    public ChestRewardClassFilter(IDataManager dataManager, ClientLanguage clientLanguage)
    {
        this.dataManager = dataManager;
        this.clientLanguage = clientLanguage;
    }

    public IEnumerable<ClassFilterOption> GetSelectableClassJobs()
    {
        var localizedSheet = dataManager.GetExcelSheet<ClassJob>(clientLanguage);
        return dataManager.GetExcelSheet<ClassJob>(ClientLanguage.English)
            .Where(classJob => classJob.RowId != 0
                && !string.IsNullOrWhiteSpace(classJob.Name.ToString())
                && !string.IsNullOrWhiteSpace(classJob.Abbreviation.ToString())
                && GetClassJobCategoryProperty(classJob.Abbreviation.ToString()) != null)
            .GroupBy(classJob => classJob.RowId)
            .Select(group => group.First())
            .Select(classJob =>
            {
                var label = classJob.Name.ToString();
                if (localizedSheet.TryGetRow(classJob.RowId, out var localizedClassJob)
                    && !string.IsNullOrWhiteSpace(localizedClassJob.Name.ToString()))
                {
                    label = localizedClassJob.Name.ToString();
                }

                return new ClassFilterOption
                {
                    RowId = classJob.RowId,
                    Label = label,
                    Abbreviation = classJob.Abbreviation.ToString(),
                };
            });
    }

    public IEnumerable<ClassFilterOption> GetGroupFilterOptions()
    {
        return
        [
            new() { RowId = AllClassesGroupFilter, Label = "All Classes" },
            new() { RowId = TanksGroupFilter, Label = "Tanks" },
            new() { RowId = HealersGroupFilter, Label = "Healers" },
            new() { RowId = MeleeGroupFilter, Label = "Melee" },
            new() { RowId = PhysicalRangedGroupFilter, Label = "Physical Ranged" },
            new() { RowId = CastersGroupFilter, Label = "Casters" },
            new() { RowId = CraftersGroupFilter, Label = "Crafters" },
            new() { RowId = GatherersGroupFilter, Label = "Gatherers" },
        ];
    }

    public MatchResult GetCompatibleFilterIds(ClassJobCategory category)
    {
        var compatibleClassJobIds = new HashSet<uint>();
        var directClassJobIds = new HashSet<uint>();
        var groupFilterIds = new HashSet<uint>();
        var englishCategoryName = GetEnglishClassJobCategoryName(category.RowId);

        foreach (var classJob in GetSelectableClassJobs())
        {
            var property = GetClassJobCategoryProperty(classJob.Abbreviation);
            if (property?.GetValue(category) is true)
            {
                compatibleClassJobIds.Add(classJob.RowId);
                if (IsDirectClassJobMatch(classJob.Abbreviation, englishCategoryName, category))
                {
                    directClassJobIds.Add(classJob.RowId);
                }

                if (dataManager.GetExcelSheet<ClassJob>(ClientLanguage.English).TryGetRow(classJob.RowId, out var classJobRow))
                {
                    groupFilterIds.UnionWith(GetGroupFilterIds(classJobRow));
                }
            }
        }

        return new MatchResult
        {
            CompatibleClassJobIds = compatibleClassJobIds,
            DirectClassJobIds = directClassJobIds,
            GroupFilterIds = groupFilterIds,
        };
    }

    public static bool IsGroupFilter(uint filterId)
    {
        return filterId >= AllClassesGroupFilter;
    }

    private string GetEnglishClassJobCategoryName(uint rowId)
    {
        if (dataManager.GetExcelSheet<ClassJobCategory>(ClientLanguage.English).TryGetRow(rowId, out var englishCategory))
        {
            return englishCategory.Name.ToString();
        }

        return string.Empty;
    }

    private static PropertyInfo? GetClassJobCategoryProperty(string abbreviation)
    {
        if (string.IsNullOrWhiteSpace(abbreviation))
        {
            return null;
        }

        return typeof(ClassJobCategory).GetProperty(abbreviation.Trim().ToUpperInvariant(), BindingFlags.Public | BindingFlags.Instance);
    }

    private static IEnumerable<uint> GetGroupFilterIds(ClassJob classJob)
    {
        var groupIds = new List<uint> { AllClassesGroupFilter };

        if (classJob.Role == 1)
        {
            groupIds.Add(TanksGroupFilter);
        }

        if (classJob.Role == 4)
        {
            groupIds.Add(HealersGroupFilter);
        }

        if (classJob.Role == 2)
        {
            groupIds.Add(MeleeGroupFilter);
        }

        if (classJob.Role == 3 && classJob.PrimaryStat == 2)
        {
            groupIds.Add(PhysicalRangedGroupFilter);
        }

        if (classJob.Role == 3 && classJob.PrimaryStat == 4)
        {
            groupIds.Add(CastersGroupFilter);
        }

        var classJobCategoryName = classJob.ClassJobCategory.ValueNullable?.Name.ToString() ?? string.Empty;
        if (string.Equals(classJobCategoryName, "Disciple of the Hand", StringComparison.OrdinalIgnoreCase))
        {
            groupIds.Add(CraftersGroupFilter);
        }

        if (string.Equals(classJobCategoryName, "Disciple of the Land", StringComparison.OrdinalIgnoreCase))
        {
            groupIds.Add(GatherersGroupFilter);
        }

        return groupIds;
    }

    private static bool IsDirectClassJobMatch(string abbreviation, string englishCategoryName, ClassJobCategory category)
    {
        if (string.IsNullOrWhiteSpace(abbreviation))
        {
            return false;
        }

        if (CountEnabledClassJobs(category) == 1)
        {
            return true;
        }

        return englishCategoryName.Contains(abbreviation, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountEnabledClassJobs(ClassJobCategory category)
    {
        return typeof(ClassJobCategory)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(property =>
                property.PropertyType == typeof(bool)
                && !property.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)
                && property.GetValue(category) is true);
    }
}
