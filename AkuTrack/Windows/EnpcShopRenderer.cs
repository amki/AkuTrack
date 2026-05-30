using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AkuTrack.Windows;

internal sealed class EnpcShopRenderer
{
    private readonly ITextureProvider textureProvider;
    private readonly Vector2 itemIconSize;
    private string selectedEnpcShopKey = string.Empty;
    private int selectedGcSubShopIndex = -1;

    public EnpcShopRenderer(ITextureProvider textureProvider, Vector2 itemIconSize)
    {
        this.textureProvider = textureProvider;
        this.itemIconSize = itemIconSize;
    }

    public void Draw(List<EnpcShopResolver.ShopEntry> shops)
    {
        if (string.IsNullOrWhiteSpace(selectedEnpcShopKey) || shops.All(shop => !string.Equals(shop.Key, selectedEnpcShopKey, StringComparison.Ordinal)))
        {
            selectedEnpcShopKey = shops[0].Key;
            selectedGcSubShopIndex = -1;
        }

        var shopLabels = shops.Select(shop => shop.Label).ToArray();
        var selectedShopIndex = Math.Max(0, shops.FindIndex(shop => string.Equals(shop.Key, selectedEnpcShopKey, StringComparison.Ordinal)));

        ImGui.SetNextItemWidth(280.0f);
        if (ImGui.Combo("Shop", ref selectedShopIndex, shopLabels, shopLabels.Length))
        {
            selectedEnpcShopKey = shops[selectedShopIndex].Key;
            selectedGcSubShopIndex = -1;
        }

        var selectedShop = shops[selectedShopIndex];
        if (selectedShop.Type == "GCShop" && selectedShop.GcSubShops.Count > 0)
        {
            if (selectedGcSubShopIndex < 0 || selectedGcSubShopIndex >= selectedShop.GcSubShops.Count)
            {
                selectedGcSubShopIndex = 0;
            }

            var subShopLabels = selectedShop.GcSubShops.Select(subShop => subShop.Label).ToArray();
            ImGui.SetNextItemWidth(280.0f);
            ImGui.Combo("Sub Shop", ref selectedGcSubShopIndex, subShopLabels, subShopLabels.Length);
            DrawShopItems(selectedShop.GcSubShops[selectedGcSubShopIndex].Items);
            return;
        }

        DrawShopItems(selectedShop.Items);
    }

    private void DrawShopItems(List<EnpcShopResolver.ShopItemEntry> items)
    {
        if (items.Count == 0)
        {
            ImGui.TextDisabled("No shop entries found.");
            return;
        }

        foreach (var item in items)
        {
            var texture = textureProvider.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrEmpty();
            ImGui.Image(texture.Handle, itemIconSize);
            ImGui.SameLine();
            ImGui.BeginGroup();
            try
            {
                ImGui.TextUnformatted(item.Name);
                if (item.Amount > 1)
                {
                    ImGui.TextDisabled($"Amount: {item.Amount}");
                }

                if (!string.IsNullOrWhiteSpace(item.RequiredRank))
                {
                    ImGui.TextDisabled(item.RequiredRank);
                }

                foreach (var cost in item.Costs)
                {
                    var costTexture = textureProvider.GetFromGameIcon(new GameIconLookup(cost.IconId)).GetWrapOrEmpty();
                    ImGui.Image(costTexture.Handle, new Vector2(22, 22));
                    ImGui.SameLine();
                    ImGui.TextDisabled($"{cost.Amount} {cost.Name}");
                }
            }
            finally
            {
                ImGui.EndGroup();
            }
        }
    }
}
