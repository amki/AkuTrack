using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AkuTrack.Managers;

public sealed class AllaganToolsIpc
{
    private static readonly uint[] AllInventoryTypes = Enum.GetValues<InventoryType>()
        .Select(inventoryType => (uint)inventoryType)
        .Distinct()
        .ToArray();

    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> isInitialized;
    private readonly ICallGateSubscriber<uint, bool, uint[], uint> itemCountOwned;
    private readonly Dictionary<uint, (uint Count, DateTime ExpiresAt)> ownedCountCache = new();
    private DateTime nextRetryAt = DateTime.MinValue;

    public AllaganToolsIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        isInitialized = pluginInterface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        itemCountOwned = pluginInterface.GetIpcSubscriber<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
    }

    public bool TryGetOwnedItemCount(uint itemId, out uint count, bool currentCharacterOnly = false)
    {
        count = 0;
        var now = DateTime.UtcNow;
        if (ownedCountCache.TryGetValue(itemId, out var cached) && cached.ExpiresAt > now)
        {
            count = cached.Count;
            return true;
        }

        if (nextRetryAt > now)
        {
            return false;
        }

        try
        {
            if (!isInitialized.InvokeFunc())
            {
                nextRetryAt = now.AddSeconds(5);
                return false;
            }

            count = itemCountOwned.InvokeFunc(itemId, currentCharacterOnly, AllInventoryTypes);
            ownedCountCache[itemId] = (count, now.AddSeconds(2));
            return true;
        }
        catch (Exception ex)
        {
            nextRetryAt = now.AddSeconds(10);
            log.Verbose(ex, "Allagan Tools IPC is unavailable or failed while querying owned item count for {ItemId}", itemId);
            return false;
        }
    }
}
