namespace AutoFpl.Api.Sources;

internal sealed record FplFormForecastPayload(
    string SeasonCode,
    int Gameweek,
    byte[] Evidence,
    string ContentSha256,
    string Transport,
    string ExtractionVersion,
    string? ProviderPayloadSha256,
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
