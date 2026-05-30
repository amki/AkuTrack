using System.Collections.Generic;
using Newtonsoft.Json;

namespace AkuTrack.ApiTypes
{
    public class ChestDropCategory
    {
        public List<ChestDropExpansion>? Expansions { get; set; }
    }

    public class ChestDropExpansion
    {
        public List<ChestDropHeader>? Headers { get; set; }
    }

    public class ChestDropHeader
    {
        public List<ChestDropDuty>? Duties { get; set; }
    }

    public class ChestDropDuty
    {
        public string? Name { get; set; }
        public List<ChestDropEntry>? Chests { get; set; }
    }

    public class ChestDropEntry
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public uint MapId { get; set; }
        public uint TerritoryId { get; set; }
        public string? PlaceNameSub { get; set; }
        public List<ChestDropReward>? Rewards { get; set; }

        [JsonIgnore]
        public string? DutyName { get; set; }
    }

    public class ChestDropReward
    {
        public uint Id { get; set; }
        public int Amount { get; set; }
        public double Pct { get; set; }
        public int Total { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
    }
}
