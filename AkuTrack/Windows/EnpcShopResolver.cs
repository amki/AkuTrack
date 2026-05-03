using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AkuTrack.Windows;

public sealed class EnpcShopResolver
{
    public const int DefaultEnpcIconId = 60424;
    public const int TripleTriadEnpcIconId = 66471;

    private const uint GilIconId = 65002;
    private const uint FreeCompanyCreditIconId = 65011;
    private static readonly string[] TripleTriadKeywords =
    [
        "triple triad",
        "triple triade",
        "triad",
        "トリプルトライアド",
    ];

    private static readonly Dictionary<uint, uint> TomestoneItems = new()
    {
        [1] = 28,
        [2] = 48,
        [3] = 49,
    };

    private readonly IDataManager dataManager;
    private readonly ClientLanguage clientLanguage;
    private readonly Dictionary<uint, int> iconCache = [];

    internal sealed class ShopEntry
    {
        public required string Key { get; init; }
        public required string Type { get; init; }
        public required string Label { get; init; }
        public string TopicLabel { get; init; } = string.Empty;
        public List<ShopItemEntry> Items { get; init; } = [];
        public List<GcSubShop> GcSubShops { get; init; } = [];
    }

    internal sealed class GcSubShop
    {
        public required string Label { get; init; }
        public List<ShopItemEntry> Items { get; init; } = [];
    }

    internal sealed class ShopItemEntry
    {
        public required string Name { get; init; }
        public required uint IconId { get; init; }
        public string RequiredRank { get; init; } = string.Empty;
        public uint Amount { get; init; } = 1;
        public List<ShopCostEntry> Costs { get; init; } = [];
    }

    internal sealed class ShopCostEntry
    {
        public required string Name { get; init; }
        public required uint IconId { get; init; }
        public required uint Amount { get; init; }
    }

    public EnpcShopResolver(IDataManager dataManager, ClientLanguage clientLanguage)
    {
        this.dataManager = dataManager;
        this.clientLanguage = clientLanguage;
    }

    internal List<ShopEntry> Resolve(uint enpcId)
    {
        if (!dataManager.GetExcelSheet<ENpcBase>().TryGetRow(enpcId, out var enpcBase))
        {
            return [];
        }

        var shops = new List<ShopEntry>();

        foreach (var rowRef in enpcBase.ENpcData)
        {
            if (rowRef.RowId == 0)
            {
                continue;
            }

            if (rowRef.TryGetValue<TopicSelect>(out var topicSelect))
            {
                shops.AddRange(ResolveTopicSelect(topicSelect));
                continue;
            }

            if (TryResolveShop(rowRef, null, out var shopEntry))
            {
                shops.Add(shopEntry);
            }
        }

        return shops
            .GroupBy(shop => shop.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    internal int GetPreferredMapIconId(uint enpcId)
    {
        if (iconCache.TryGetValue(enpcId, out var cachedIconId))
        {
            return cachedIconId;
        }

        var iconId = ResolvePreferredMapIconId(enpcId);
        iconCache[enpcId] = iconId;
        return iconId;
    }

    private IEnumerable<ShopEntry> ResolveTopicSelect(TopicSelect topicSelect)
    {
        var topicLabel = topicSelect.Name.ToString();
        if (string.IsNullOrWhiteSpace(topicLabel))
        {
            topicLabel = $"Topic {topicSelect.RowId}";
        }

        foreach (var rowRef in topicSelect.Shop)
        {
            if (rowRef.RowId == 0)
            {
                continue;
            }

            if (TryResolveShop(rowRef, topicLabel, out var shopEntry))
            {
                yield return shopEntry;
            }
        }
    }

    private int ResolvePreferredMapIconId(uint enpcId)
    {
        if (!dataManager.GetExcelSheet<ENpcBase>().TryGetRow(enpcId, out var enpcBase))
        {
            return DefaultEnpcIconId;
        }

        foreach (var rowRef in enpcBase.ENpcData)
        {
            if (rowRef.RowId == 0)
            {
                continue;
            }

            if (IsTripleTriadRow(rowRef))
            {
                return TripleTriadEnpcIconId;
            }

            if (rowRef.TryGetValue<TopicSelect>(out var topicSelect))
            {
                foreach (var topicRowRef in topicSelect.Shop)
                {
                    if (topicRowRef.RowId == 0)
                    {
                        continue;
                    }

                    if (IsTripleTriadRow(topicRowRef))
                    {
                        return TripleTriadEnpcIconId;
                    }
                }
            }
        }

        return DefaultEnpcIconId;
    }

    private bool IsTripleTriadRow(Lumina.Excel.RowRef rowRef)
    {
        if (rowRef.TryGetValue<TripleTriad>(out _))
        {
            return true;
        }

        return rowRef.TryGetValue<SpecialShop>(out var specialShop) && IsTripleTriadSpecialShop(specialShop);
    }

    private bool IsTripleTriadSpecialShop(SpecialShop shop)
    {
        if (HasTripleTriadKeyword(shop.Name.ToString()))
        {
            return true;
        }

        if (dataManager.GetExcelSheet<SpecialShop>(clientLanguage).TryGetRow(shop.RowId, out var localizedShop)
            && HasTripleTriadKeyword(localizedShop.Name.ToString()))
        {
            return true;
        }

        if (dataManager.GetExcelSheet<SpecialShop>(ClientLanguage.English).TryGetRow(shop.RowId, out var englishShop)
            && HasTripleTriadKeyword(englishShop.Name.ToString()))
        {
            return true;
        }

        return false;
    }

    private static bool HasTripleTriadKeyword(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return TripleTriadKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryResolveShop(Lumina.Excel.RowRef rowRef, string? topicLabel, out ShopEntry shopEntry)
    {
        if (rowRef.TryGetValue<SpecialShop>(out var specialShop))
        {
            shopEntry = BuildSpecialShopEntry(specialShop, topicLabel);
            return true;
        }

        if (rowRef.TryGetValue<GilShop>(out var gilShop))
        {
            shopEntry = BuildGilShopEntry(gilShop, topicLabel);
            return true;
        }

        if (rowRef.TryGetValue<FccShop>(out var fccShop))
        {
            shopEntry = BuildFccShopEntry(fccShop, topicLabel);
            return true;
        }

        if (rowRef.TryGetValue<GCShop>(out var gcShop))
        {
            shopEntry = BuildGcShopEntry(gcShop, topicLabel);
            return true;
        }

        shopEntry = null!;
        return false;
    }

    private ShopEntry BuildSpecialShopEntry(SpecialShop shop, string? topicLabel)
    {
        var items = new List<ShopItemEntry>();

        foreach (var entry in shop.Item)
        {
            var costs = BuildSpecialShopCosts(shop.UseCurrencyType, entry.ItemCosts).ToList();
            foreach (var receive in entry.ReceiveItems)
            {
                var itemRow = receive.Item.ValueNullable;
                if (itemRow == null || itemRow.Value.RowId == 0)
                {
                    continue;
                }

                items.Add(new ShopItemEntry
                {
                    Name = itemRow.Value.Name.ToString(),
                    IconId = itemRow.Value.Icon,
                    Amount = receive.ReceiveCount == 0 ? 1u : receive.ReceiveCount,
                    Costs = costs,
                });
            }
        }

        return new ShopEntry
        {
            Key = BuildKey("SpecialShop", shop.RowId, topicLabel),
            Type = "SpecialShop",
            Label = BuildLabel(shop.Name.ToString(), "SpecialShop", topicLabel),
            TopicLabel = topicLabel ?? string.Empty,
            Items = items,
        };
    }

    private IEnumerable<ShopCostEntry> BuildSpecialShopCosts(byte useCurrencyType, IEnumerable<SpecialShop.ItemStruct.ItemCostsStruct> itemCosts)
    {
        foreach (var itemCost in itemCosts)
        {
            if (itemCost.CurrencyCost == 0)
            {
                continue;
            }

            var itemRow = itemCost.ItemCost.ValueNullable;
            if (itemRow == null || itemRow.Value.RowId == 0)
            {
                if (TomestoneItems.TryGetValue(itemCost.ItemCost.RowId, out var tomestoneItemId)
                    && dataManager.GetExcelSheet<Item>(clientLanguage).TryGetRow(tomestoneItemId, out var tomestoneItem))
                {
                    yield return new ShopCostEntry
                    {
                        Name = tomestoneItem.Name.ToString(),
                        IconId = tomestoneItem.Icon,
                        Amount = itemCost.CurrencyCost,
                    };
                }

                continue;
            }

            yield return new ShopCostEntry
            {
                Name = itemRow.Value.Name.ToString(),
                IconId = itemRow.Value.Icon,
                Amount = itemCost.CurrencyCost,
            };
        }
    }

    private ShopEntry BuildGilShopEntry(GilShop shop, string? topicLabel)
    {
        var items = new List<ShopItemEntry>();
        var gilShopItems = dataManager.GetSubrowExcelSheet<GilShopItem>();
        if (gilShopItems.TryGetRow(shop.RowId, out var subrows))
        {
            foreach (var row in subrows)
            {
                var itemRow = row.Item.ValueNullable;
                if (itemRow == null || itemRow.Value.RowId == 0)
                {
                    continue;
                }

                items.Add(new ShopItemEntry
                {
                    Name = itemRow.Value.Name.ToString(),
                    IconId = itemRow.Value.Icon,
                    Costs =
                    [
                        new ShopCostEntry
                        {
                            Name = "Gil",
                            IconId = GilIconId,
                            Amount = itemRow.Value.PriceMid,
                        }
                    ],
                });
            }
        }

        return new ShopEntry
        {
            Key = BuildKey("GilShop", shop.RowId, topicLabel),
            Type = "GilShop",
            Label = BuildLabel(shop.Name.ToString(), "GilShop", topicLabel),
            TopicLabel = topicLabel ?? string.Empty,
            Items = items,
        };
    }

    private ShopEntry BuildFccShopEntry(FccShop shop, string? topicLabel)
    {
        var items = new List<ShopItemEntry>();
        foreach (var row in shop.ItemData)
        {
            var itemRow = row.Item.ValueNullable;
            if (itemRow == null || itemRow.Value.RowId == 0)
            {
                continue;
            }

            items.Add(new ShopItemEntry
            {
                Name = itemRow.Value.Name.ToString(),
                IconId = itemRow.Value.Icon,
                RequiredRank = row.FCRankRequired.RowId == 0 ? string.Empty : $"FC Rank {row.FCRankRequired.RowId}",
                Costs =
                [
                    new ShopCostEntry
                    {
                        Name = "Free Company Credits",
                        IconId = FreeCompanyCreditIconId,
                        Amount = row.Cost,
                    }
                ],
            });
        }

        return new ShopEntry
        {
            Key = BuildKey("FccShop", shop.RowId, topicLabel),
            Type = "FccShop",
            Label = BuildLabel(shop.Name.ToString(), "FccShop", topicLabel),
            TopicLabel = topicLabel ?? string.Empty,
            Items = items,
        };
    }

    private ShopEntry BuildGcShopEntry(GCShop shop, string? topicLabel)
    {
        var subShops = new List<GcSubShop>();
        var categories = dataManager.GetExcelSheet<GCScripShopCategory>();
        var items = dataManager.GetSubrowExcelSheet<GCScripShopItem>();
        var localizedItemSheet = dataManager.GetExcelSheet<Item>(clientLanguage);
        var localizedGrandCompanySheet = dataManager.GetExcelSheet<GrandCompany>(clientLanguage);

        foreach (var category in categories)
        {
            if (category.RowId == 0)
            {
                continue;
            }

            if (!items.TryGetRow(category.RowId, out var categoryItems))
            {
                continue;
            }

            var subShopItems = new List<ShopItemEntry>();
            foreach (var row in categoryItems)
            {
                var itemRow = row.Item.ValueNullable;
                if (itemRow == null || itemRow.Value.RowId == 0)
                {
                    continue;
                }

                var companyName = category.GrandCompany.ValueNullable?.Name.ToString() ?? $"GC {category.GrandCompany.RowId}";
                var sealItemId = category.GrandCompany.RowId switch
                {
                    1 => 20u,
                    2 => 21u,
                    3 => 22u,
                    _ => 0u,
                };

                var costs = new List<ShopCostEntry>();
                if (sealItemId != 0 && localizedItemSheet.TryGetRow(sealItemId, out var sealItem))
                {
                    costs.Add(new ShopCostEntry
                    {
                        Name = sealItem.Name.ToString(),
                        IconId = sealItem.Icon,
                        Amount = row.CostGCSeals,
                    });
                }

                subShopItems.Add(new ShopItemEntry
                {
                    Name = itemRow.Value.Name.ToString(),
                    IconId = itemRow.Value.Icon,
                    RequiredRank = row.RequiredGrandCompanyRank.RowId == 0 ? string.Empty : $"Rank {row.RequiredGrandCompanyRank.RowId}",
                    Costs = costs,
                });
            }

            if (subShopItems.Count == 0)
            {
                continue;
            }

            var companyLabel = localizedGrandCompanySheet.TryGetRow(category.GrandCompany.RowId, out var company)
                ? company.Name.ToString()
                : $"GC {category.GrandCompany.RowId}";
            subShops.Add(new GcSubShop
            {
                Label = $"{companyLabel} · Sub {category.RowId}",
                Items = subShopItems,
            });
        }

        return new ShopEntry
        {
            Key = BuildKey("GCShop", shop.RowId, topicLabel),
            Type = "GCShop",
            Label = BuildLabel(shop.GrandCompany.ValueNullable?.Name.ToString() ?? "GC Shop", "GCShop", topicLabel),
            TopicLabel = topicLabel ?? string.Empty,
            GcSubShops = subShops,
        };
    }

    private static string BuildKey(string type, uint rowId, string? topicLabel)
    {
        return string.IsNullOrWhiteSpace(topicLabel)
            ? $"{type}:{rowId}"
            : $"{topicLabel}:{type}:{rowId}";
    }

    private static string BuildLabel(string shopName, string type, string? topicLabel)
    {
        var name = string.IsNullOrWhiteSpace(shopName) ? type : shopName;
        return string.IsNullOrWhiteSpace(topicLabel)
            ? $"[{type}] {name}"
            : $"[{type}] {name} ({topicLabel})";
    }
}
