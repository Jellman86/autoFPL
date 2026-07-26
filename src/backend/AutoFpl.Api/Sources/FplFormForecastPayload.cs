namespace AutoFpl.Api.Sources;

internal sealed record FplFormForecastPayload(
    string SeasonCode,
    int Gameweek,
    byte[] Html,
    string ContentSha256,
    IReadOnlyList<FplFormFixturePrediction> Predictions);

internal sealed record FplFormFixturePrediction(
    int SourcePlayerId,
    int FixtureId,
    string PlayerName,
    string TeamName,
    string Position,
    string KickoffLocal,
    decimal PredictedPoints,
    decimal? AppearanceProbability);
