namespace AkuTrack;

public readonly record struct SightseeingLogEntryInfo(
    uint RowId,
    string Name,
    string Description,
    string Time,
    string Weather,
    string Emote);
