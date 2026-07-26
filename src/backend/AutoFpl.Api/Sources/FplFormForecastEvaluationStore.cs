using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Sources;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Sources;

public sealed class FplFormForecastEvaluationStore
{
    public const string EvaluatorVersion = "fpl-form-external-evaluation-v1";
    private const string PublishedModel = "fpl-form-published-conditional-points";
    private const string AdjustedModel = "autofpl-appearance-probability-adjusted-points";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly DatabaseOptions _options;
    private readonly FplFormIdentityCoverageStore _identityStore;

    public FplFormForecastEvaluationStore(
        DatabaseOptions options,
        FplFormIdentityCoverageStore identityStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _identityStore = identityStore
            ?? throw new ArgumentNullException(nameof(identityStore));
    }

    public async Task<FplFormForecastEvaluationDocument> EvaluateAsync(
        string? seasonCode = null,
        CancellationToken cancellationToken = default)
    {
        if (seasonCode is not null
            && (string.IsNullOrWhiteSpace(seasonCode) || seasonCode.Length > 16))
        {
            throw new ArgumentException(
                "Season code must contain between 1 and 16 characters.",
                nameof(seasonCode));
        }

        await using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder(_options.ConnectionString)
            {
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        await connection.OpenAsync(cancellationToken);
        List<ForecastHeader> forecasts = await ReadForecastsAsync(
            connection,
            seasonCode,
            cancellationToken);
        Dictionary<(string SeasonCode, int Gameweek), OutcomeHeader> outcomes =
            await ReadLatestOutcomesAsync(connection, seasonCode, cancellationToken);
        var folds = new List<FplFormForecastEvaluationFoldDocument>();
        var exclusions = new List<FplFormForecastEvaluationExclusionDocument>();
        var publishedSamples = new List<EvaluationSample>();
        var adjustedSamples = new List<EvaluationSample>();
        int candidateCount = 0;

        foreach (IGrouping<(string SeasonCode, int Gameweek), ForecastHeader> group
                 in forecasts.GroupBy(item => (item.SeasonCode, item.Gameweek)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidateCount++;
            FplFormIdentityResolution? resolution = null;
            ForecastHeader? selected = null;
            ForecastHeader latest = group
                .OrderByDescending(item => item.AvailableAtUtc)
                .ThenByDescending(item => item.CaptureId)
                .First();
            foreach (ForecastHeader forecast in group
                         .OrderByDescending(item => item.AvailableAtUtc)
                         .ThenByDescending(item => item.CaptureId))
            {
                FplFormIdentityResolution? candidate =
                    await _identityStore.GetResolutionAsync(
                        forecast.CaptureId,
                        cancellationToken);
                if (candidate is null)
                {
                    continue;
                }

                if (candidate.Document.Status == "forecast-after-deadline")
                {
                    continue;
                }

                selected = forecast;
                resolution = candidate;
                break;
            }

            if (selected is null || resolution is null)
            {
                exclusions.Add(
                    new(
                        latest.CaptureId,
                        latest.ContentSha256,
                        latest.SeasonCode,
                        latest.Gameweek,
                        "no-deadline-eligible-forecast"));
                continue;
            }

            if (!resolution.Document.IsComplete)
            {
                exclusions.Add(
                    new(
                        selected.CaptureId,
                        selected.ContentSha256,
                        selected.SeasonCode,
                        selected.Gameweek,
                        resolution.Document.Status));
                continue;
            }

            if (!outcomes.TryGetValue(group.Key, out OutcomeHeader? outcome))
            {
                exclusions.Add(
                    new(
                        selected.CaptureId,
                        selected.ContentSha256,
                        selected.SeasonCode,
                        selected.Gameweek,
                        "official-outcome-unavailable"));
                continue;
            }

            if (resolution.Document.DeadlineUtc is not DateTimeOffset deadlineUtc
                || outcome.AvailableAtUtc <= deadlineUtc)
            {
                exclusions.Add(
                    new(
                        selected.CaptureId,
                        selected.ContentSha256,
                        selected.SeasonCode,
                        selected.Gameweek,
                        "official-outcome-chronology-invalid"));
                continue;
            }

            Dictionary<int, PlayerOutcome> playerOutcomes =
                await ReadPlayerOutcomesAsync(
                    connection,
                    outcome.OutcomeCaptureId,
                    cancellationToken);
            IReadOnlyList<PlayerForecast> playerForecasts = AggregatePlayers(
                resolution.Predictions);
            if (playerForecasts.Count == 0
                || playerForecasts.Any(
                    forecast => !playerOutcomes.ContainsKey(forecast.OfficialPlayerId)))
            {
                exclusions.Add(
                    new(
                        selected.CaptureId,
                        selected.ContentSha256,
                        selected.SeasonCode,
                        selected.Gameweek,
                        "official-outcome-player-coverage-incomplete"));
                continue;
            }

            var foldPublished = new List<EvaluationSample>();
            var foldAdjusted = new List<EvaluationSample>();
            foreach (PlayerForecast playerForecast in playerForecasts)
            {
                PlayerOutcome actual = playerOutcomes[playerForecast.OfficialPlayerId];
                var published = new EvaluationSample(
                    selected.SeasonCode,
                    selected.Gameweek,
                    playerForecast.OfficialPlayerId,
                    playerForecast.Position,
                    (double)playerForecast.PublishedPoints,
                    actual.TotalPoints,
                    actual.Minutes);
                foldPublished.Add(published);
                publishedSamples.Add(published);
                if (playerForecast.AdjustedPoints is decimal adjustedPoints)
                {
                    var adjusted = published with
                    {
                        Predicted = (double)adjustedPoints,
                    };
                    foldAdjusted.Add(adjusted);
                    adjustedSamples.Add(adjusted);
                }
            }

            folds.Add(
                new(
                    selected.SeasonCode,
                    selected.Gameweek,
                    deadlineUtc,
                    selected.CaptureId,
                    selected.AvailableAtUtc,
                    selected.ContentSha256,
                    resolution.Document.OfficialCaptureId!.Value,
                    resolution.Document.OfficialBootstrapSha256!,
                    resolution.Document.OfficialFixturesSha256!,
                    outcome.OutcomeCaptureId,
                    outcome.AvailableAtUtc,
                    outcome.LiveSha256,
                    playerForecasts.Count,
                    resolution.Predictions.Count,
                    foldAdjusted.Count,
                    CalculateMetrics(foldPublished),
                    foldAdjusted.Count == 0 ? null : CalculateMetrics(foldAdjusted)));
        }

        folds.Sort(
            (left, right) =>
            {
                int season = StringComparer.Ordinal.Compare(
                    left.SeasonCode,
                    right.SeasonCode);
                return season != 0 ? season : left.Gameweek.CompareTo(right.Gameweek);
            });
        exclusions.Sort(
            (left, right) =>
            {
                int season = StringComparer.Ordinal.Compare(
                    left.SeasonCode,
                    right.SeasonCode);
                if (season != 0)
                {
                    return season;
                }

                int gameweek = left.Gameweek.CompareTo(right.Gameweek);
                return gameweek != 0
                    ? gameweek
                    : left.ForecastCaptureId.CompareTo(right.ForecastCaptureId);
            });
        List<FplFormForecastEvaluationModelDocument> models =
            CreateModels(publishedSamples, adjustedSamples);
        string status = folds.Count == 0 ? "insufficient-data" : "complete";
        string? reason = folds.Count == 0
            ? "no-complete-forecast-outcome-pairs"
            : null;
        string dataIdentity = Hash(
            new
            {
                seasonCode,
                folds = folds.Select(
                    fold => new
                    {
                        fold.SeasonCode,
                        fold.Gameweek,
                        fold.ForecastCaptureId,
                        fold.ForecastContentSha256,
                        fold.IdentityCaptureId,
                        fold.IdentityBootstrapSha256,
                        fold.IdentityFixturesSha256,
                        fold.OutcomeCaptureId,
                        fold.OutcomeLiveSha256,
                    }),
                exclusions,
            });
        string runIdentity = Hash(
            new
            {
                evaluatorVersion = EvaluatorVersion,
                researchStatus = "exploratory-external-baseline-not-promoted",
                dataIdentitySha256 = dataIdentity,
                status,
                reason,
                folds,
                models,
            });
        return new(
            "1.0",
            EvaluatorVersion,
            "exploratory-external-baseline-not-promoted",
            status,
            reason,
            seasonCode,
            candidateCount,
            folds.Count,
            dataIdentity,
            runIdentity,
            folds,
            exclusions,
            models);
    }

    private static IReadOnlyList<PlayerForecast> AggregatePlayers(
        IReadOnlyList<FplFormResolvedPrediction> predictions) =>
        predictions
            .GroupBy(prediction => prediction.OfficialPlayerId)
            .Select(
                group =>
                {
                    FplFormResolvedPrediction first = group.First();
                    bool hasCompleteProbabilities = group.All(
                        prediction => prediction.AppearanceProbability is not null);
                    return new PlayerForecast(
                        first.OfficialPlayerId,
                        first.Position,
                        group.Sum(prediction => prediction.PredictedPoints),
                        hasCompleteProbabilities
                            ? group.Sum(
                                prediction =>
                                    prediction.PredictedPoints
                                    * prediction.AppearanceProbability!.Value)
                            : null);
                })
            .OrderBy(forecast => forecast.OfficialPlayerId)
            .ToList();

    private static List<FplFormForecastEvaluationModelDocument> CreateModels(
        IReadOnlyList<EvaluationSample> published,
        IReadOnlyList<EvaluationSample> adjusted)
    {
        var models = new List<FplFormForecastEvaluationModelDocument>();
        if (published.Count > 0)
        {
            models.Add(
                CreateModel(
                    PublishedModel,
                    "Provider-published points conditional on appearing; zero-minute "
                        + "outcomes remain in the scored population.",
                    published,
                    0));
        }

        if (published.Count > 0)
        {
            models.Add(
                CreateModel(
                    AdjustedModel,
                    "autoFPL-derived predicted points multiplied fixture-by-fixture "
                        + "by the provider appearance probability.",
                    adjusted,
                    published.Count - adjusted.Count));
        }

        return models;
    }

    private static FplFormForecastEvaluationModelDocument CreateModel(
        string name,
        string interpretation,
        IReadOnlyList<EvaluationSample> samples,
        int missingPlayerCount)
    {
        if (samples.Count == 0)
        {
            return new(
                name,
                interpretation,
                0,
                missingPlayerCount,
                null,
                null,
                []);
        }

        List<EvaluationSample> zeroMinutes = samples
            .Where(sample => sample.Minutes == 0)
            .ToList();
        List<FplFormForecastEvaluationSliceDocument> slices = samples
            .GroupBy(sample => sample.Position)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(
                group => new FplFormForecastEvaluationSliceDocument(
                    group.Key,
                    CalculateMetrics(group)))
            .ToList();
        return new(
            name,
            interpretation,
            samples.Count,
            missingPlayerCount,
            CalculateMetrics(samples),
            zeroMinutes.Count == 0 ? null : CalculateMetrics(zeroMinutes),
            slices);
    }

    private static FplFormForecastEvaluationMetricsDocument CalculateMetrics(
        IEnumerable<EvaluationSample> source)
    {
        List<EvaluationSample> samples = source.ToList();
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("Evaluation metrics require at least one sample.");
        }

        double mae = samples.Average(sample => Math.Abs(sample.Predicted - sample.Actual));
        double mse = samples.Average(
            sample => Math.Pow(sample.Predicted - sample.Actual, 2));
        double bias = samples.Average(sample => sample.Predicted - sample.Actual);
        return new(
            samples.Count,
            Round(mae),
            Round(Math.Sqrt(mse)),
            Round(bias));
    }

    private static async Task<List<ForecastHeader>> ReadForecastsAsync(
        SqliteConnection connection,
        string? seasonCode,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture_id,
                season_code,
                gameweek,
                available_at_utc,
                content_sha256
            FROM fpl_form_forecast_captures
            WHERE $seasonCode IS NULL OR season_code = $seasonCode
            ORDER BY season_code, gameweek, julianday(available_at_utc) DESC, capture_id DESC;
            """;
        command.Parameters.AddWithValue(
            "$seasonCode",
            seasonCode is null ? DBNull.Value : seasonCode);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var forecasts = new List<ForecastHeader>();
        while (await reader.ReadAsync(cancellationToken))
        {
            forecasts.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    ParseTimestamp(reader.GetString(3)),
                    reader.GetString(4)));
        }

        return forecasts;
    }

    private static async Task<Dictionary<(string SeasonCode, int Gameweek), OutcomeHeader>>
        ReadLatestOutcomesAsync(
            SqliteConnection connection,
            string? seasonCode,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                outcome_capture_id,
                season_code,
                gameweek,
                available_at_utc,
                live_sha256
            FROM official_fpl_outcome_captures AS outcome
            WHERE ($seasonCode IS NULL OR outcome.season_code = $seasonCode)
                AND NOT EXISTS (
                    SELECT 1
                    FROM official_fpl_outcome_captures AS newer
                    WHERE newer.season_code = outcome.season_code
                        AND newer.gameweek = outcome.gameweek
                        AND (
                            julianday(newer.available_at_utc)
                                > julianday(outcome.available_at_utc)
                            OR (
                                julianday(newer.available_at_utc)
                                    = julianday(outcome.available_at_utc)
                                AND newer.outcome_capture_id
                                    > outcome.outcome_capture_id
                            )
                        )
                )
            ORDER BY season_code, gameweek;
            """;
        command.Parameters.AddWithValue(
            "$seasonCode",
            seasonCode is null ? DBNull.Value : seasonCode);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var outcomes = new Dictionary<(string SeasonCode, int Gameweek), OutcomeHeader>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var outcome = new OutcomeHeader(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                ParseTimestamp(reader.GetString(3)),
                reader.GetString(4));
            outcomes.Add((outcome.SeasonCode, outcome.Gameweek), outcome);
        }

        return outcomes;
    }

    private static async Task<Dictionary<int, PlayerOutcome>> ReadPlayerOutcomesAsync(
        SqliteConnection connection,
        long outcomeCaptureId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT player_id, total_points, minutes
            FROM official_fpl_player_outcomes
            WHERE outcome_capture_id = $outcomeCaptureId
            ORDER BY player_id;
            """;
        command.Parameters.AddWithValue("$outcomeCaptureId", outcomeCaptureId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var outcomes = new Dictionary<int, PlayerOutcome>();
        while (await reader.ReadAsync(cancellationToken))
        {
            outcomes.Add(
                reader.GetInt32(0),
                new(reader.GetInt32(1), reader.GetInt32(2)));
        }

        return outcomes;
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexString(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)))
            .ToLowerInvariant();

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static double Round(double value) =>
        Math.Round(value, 6, MidpointRounding.ToEven);

    private sealed record ForecastHeader(
        long CaptureId,
        string SeasonCode,
        int Gameweek,
        DateTimeOffset AvailableAtUtc,
        string ContentSha256);

    private sealed record OutcomeHeader(
        long OutcomeCaptureId,
        string SeasonCode,
        int Gameweek,
        DateTimeOffset AvailableAtUtc,
        string LiveSha256);

    private sealed record PlayerOutcome(int TotalPoints, int Minutes);

    private sealed record PlayerForecast(
        int OfficialPlayerId,
        string Position,
        decimal PublishedPoints,
        decimal? AdjustedPoints);

    private sealed record EvaluationSample(
        string SeasonCode,
        int Gameweek,
        int PlayerId,
        string Position,
        double Predicted,
        int Actual,
        int Minutes);
}
