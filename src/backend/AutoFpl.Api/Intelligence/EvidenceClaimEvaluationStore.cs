using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using AutoFpl.Api.Persistence;
using AutoFpl.Contracts.Intelligence;

using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Intelligence;

public sealed class EvidenceClaimEvaluationStore
{
    public const string EvaluatorVersion = "evidence-start-claim-evaluation-v2";
    public const string ResearchStatus =
        "quarantined-source-evaluation-not-promoted";
    public const string ReliabilityMethod =
        "jeffreys-beta-binomial-class-balanced-v1";

    private const double LogLossEpsilon = 1e-15;
    private const double NaturalLogOfTwo = 0.6931471805599453;
    private const double JeffreysPriorShape = 0.5d;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DatabaseOptions _options;

    public EvidenceClaimEvaluationStore(DatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<EvidenceClaimEvaluationDocument> EvaluateAsync(
        string? seasonCode = null,
        CancellationToken cancellationToken = default)
    {
        if (seasonCode is not null
            && (string.IsNullOrWhiteSpace(seasonCode)
                || seasonCode.Length is < 4 or > 16))
        {
            throw new ArgumentException(
                "Season code must contain between 4 and 16 characters.",
                nameof(seasonCode));
        }

        await using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder(_options.ConnectionString)
            {
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        await connection.OpenAsync(cancellationToken);

        IReadOnlyList<OutcomeHeader> outcomes = await ReadLatestOutcomesAsync(
            connection,
            seasonCode,
            cancellationToken);
        var folds = new List<EvidenceClaimEvaluationFoldDocument>();
        var exclusions = new List<EvidenceClaimEvaluationExclusionDocument>();
        var allSamples = new List<EvaluationSample>();
        var dataClaims = new List<object>();

        foreach (OutcomeHeader outcome in outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome.AvailableAtUtc <= outcome.DeadlineUtc)
            {
                exclusions.Add(
                    new(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        "official-outcome-chronology-invalid"));
                continue;
            }
            IReadOnlyList<ClaimOutcomeRow> rows = await ReadClaimOutcomesAsync(
                connection,
                outcome,
                cancellationToken);
            rows = LatestSourceAssertions(rows);
            if (rows.Count == 0)
            {
                exclusions.Add(
                    new(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        "no-pre-deadline-start-claims"));
                continue;
            }
            if (rows.Any(row => row.Starts is null))
            {
                exclusions.Add(
                    new(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        "official-outcome-player-identity-incomplete"));
                continue;
            }

            List<EvaluationSample> samples = rows
                .Select(row => CreateSample(row, outcome))
                .Where(sample => sample is not null)
                .Cast<EvaluationSample>()
                .ToList();
            if (samples.Count == 0)
            {
                exclusions.Add(
                    new(
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        "no-scorable-start-claims"));
                continue;
            }

            allSamples.AddRange(samples);
            dataClaims.AddRange(
                rows.Select(
                    row => new
                    {
                        row.ClaimId,
                        row.ClaimContentSha256,
                        row.PlayerCode,
                        row.Starts,
                    }));
            folds.Add(
                new(
                    outcome.SeasonCode,
                    outcome.Gameweek,
                    outcome.DeadlineUtc,
                    outcome.OutcomeCaptureId,
                    outcome.AvailableAtUtc,
                    outcome.LiveSha256,
                    samples.Count,
                    CreateSlices(samples)));
        }

        folds.Sort(CompareFolds);
        exclusions.Sort(CompareExclusions);
        string status = folds.Count == 0 ? "insufficient-data" : "complete";
        string? reason = folds.Count == 0
            ? "no-scorable-claim-outcome-pairs"
            : null;
        IReadOnlyList<EvidenceClaimEvaluationSliceDocument> slices =
            CreateSlices(allSamples);
        string dataIdentity = Hash(
            new
            {
                seasonCode,
                outcomes = outcomes.Select(
                    outcome => new
                    {
                        outcome.SeasonCode,
                        outcome.Gameweek,
                        outcome.OutcomeCaptureId,
                        outcome.LiveSha256,
                    }),
                claims = dataClaims,
                exclusions,
            });
        string runIdentity = Hash(
            new
            {
                evaluatorVersion = EvaluatorVersion,
                researchStatus = ResearchStatus,
                reliabilityMethod = ReliabilityMethod,
                dataIdentitySha256 = dataIdentity,
                status,
                reason,
                slices,
                folds,
                exclusions,
            });
        return new(
            "1.1",
            EvaluatorVersion,
            ResearchStatus,
            ReliabilityMethod,
            status,
            reason,
            seasonCode,
            outcomes.Count,
            folds.Count,
            allSamples.Count,
            dataIdentity,
            runIdentity,
            slices,
            folds,
            exclusions);
    }

    private static IReadOnlyList<ClaimOutcomeRow> LatestSourceAssertions(
        IEnumerable<ClaimOutcomeRow> rows) =>
        rows
            .GroupBy(
                row => new SourceAssertionKey(
                    row.SourceKey,
                    row.PlayerCode))
            .Select(
                group => group
                    .OrderByDescending(row => row.AvailableAtUtc)
                    .ThenByDescending(row => row.ClaimId)
                    .First())
            .OrderBy(row => row.AvailableAtUtc)
            .ThenBy(row => row.ClaimId)
            .ToList();

    private static EvaluationSample? CreateSample(
        ClaimOutcomeRow row,
        OutcomeHeader outcome)
    {
        bool actual = row.Starts!.Value > 0;
        bool? predicted = row.ForecastProbability is not null
            ? row.ForecastProbability.Value >= 0.5m
            : row.StartStatus switch
            {
                "starts" => true,
                "does-not-start" => false,
                _ => null,
            };
        return predicted is null
            ? null
            : new(
                outcome.SeasonCode,
                outcome.Gameweek,
                row.SourceKey,
                "start",
                LeadTimeBucket(row.DeadlineUtc - row.AvailableAtUtc),
                predicted.Value,
                actual,
                row.ForecastProbability is null
                    ? null
                    : (double)row.ForecastProbability.Value);
    }

    private static IReadOnlyList<EvidenceClaimEvaluationSliceDocument> CreateSlices(
        IEnumerable<EvaluationSample> source) =>
        source
            .GroupBy(
                sample => new SliceKey(
                    sample.SourceKey,
                    sample.ClaimType,
                    sample.LeadTimeBucket))
            .OrderBy(group => group.Key.SourceKey, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ClaimType, StringComparer.Ordinal)
            .ThenBy(
                group => LeadTimeOrder(group.Key.LeadTimeBucket))
            .Select(CreateSlice)
            .ToList();

    private static EvidenceClaimEvaluationSliceDocument CreateSlice(
        IGrouping<SliceKey, EvaluationSample> group)
    {
        List<EvaluationSample> samples = group.ToList();
        List<EvaluationSample> probabilistic = samples
            .Where(sample => sample.Probability is not null)
            .ToList();
        int truePositive = samples.Count(
            sample => sample.Predicted && sample.Actual);
        int trueNegative = samples.Count(
            sample => !sample.Predicted && !sample.Actual);
        int falsePositive = samples.Count(
            sample => sample.Predicted && !sample.Actual);
        int falseNegative = samples.Count(
            sample => !sample.Predicted && sample.Actual);
        int positiveOutcomeCount = truePositive + falseNegative;
        int negativeOutcomeCount = trueNegative + falsePositive;
        double? shrunkSensitivity = ShrunkClassAccuracy(
            truePositive,
            positiveOutcomeCount);
        double? shrunkSpecificity = ShrunkClassAccuracy(
            trueNegative,
            negativeOutcomeCount);
        double? shrunkBalancedAccuracy =
            shrunkSensitivity is null || shrunkSpecificity is null
                ? null
                : Round(
                    (shrunkSensitivity.Value + shrunkSpecificity.Value)
                    / 2d);
        double? brier = probabilistic.Count == 0
            ? null
            : Round(
                probabilistic.Average(
                    sample => Math.Pow(
                        sample.Probability!.Value
                            - (sample.Actual ? 1d : 0d),
                        2)));
        double? logLoss = probabilistic.Count == 0
            ? null
            : Round(
                probabilistic.Average(
                    sample =>
                    {
                        double probability = Math.Clamp(
                            sample.Probability!.Value,
                            LogLossEpsilon,
                            1d - LogLossEpsilon);
                        return sample.Actual
                            ? -Math.Log2(probability) * NaturalLogOfTwo
                            : -Math.Log2(1d - probability) * NaturalLogOfTwo;
                    }));
        return new(
            group.Key.SourceKey,
            group.Key.ClaimType,
            group.Key.LeadTimeBucket,
            samples.Count,
            samples
                .Select(sample => (sample.SeasonCode, sample.Gameweek))
                .Distinct()
                .Count(),
            positiveOutcomeCount,
            truePositive + trueNegative,
            Round(
                (truePositive + trueNegative)
                    / (double)samples.Count),
            truePositive,
            trueNegative,
            falsePositive,
            falseNegative,
            shrunkSensitivity,
            shrunkSpecificity,
            shrunkBalancedAccuracy,
            probabilistic.Count,
            brier,
            logLoss);
    }

    private static double? ShrunkClassAccuracy(
        int correctCount,
        int outcomeCount) =>
        outcomeCount == 0
            ? null
            : Round(
                (correctCount + JeffreysPriorShape)
                / (outcomeCount + (2d * JeffreysPriorShape)));

    private static string LeadTimeBucket(TimeSpan leadTime) =>
        leadTime.TotalHours switch
        {
            < 6 => "0-6h",
            < 24 => "6-24h",
            < 72 => "24-72h",
            _ => "72h+",
        };

    private static int LeadTimeOrder(string bucket) =>
        bucket switch
        {
            "0-6h" => 0,
            "6-24h" => 1,
            "24-72h" => 2,
            _ => 3,
        };

    private static async Task<IReadOnlyList<OutcomeHeader>> ReadLatestOutcomesAsync(
        SqliteConnection connection,
        string? seasonCode,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                outcome.outcome_capture_id,
                outcome.season_code,
                outcome.gameweek,
                outcome.reference_capture_id,
                event.deadline_utc,
                outcome.available_at_utc,
                outcome.live_sha256
            FROM official_fpl_outcome_captures AS outcome
            INNER JOIN official_fpl_events AS event
                ON event.capture_id = outcome.reference_capture_id
               AND event.event_id = outcome.gameweek
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
            ORDER BY outcome.season_code, outcome.gameweek;
            """;
        command.Parameters.AddWithValue(
            "$seasonCode",
            seasonCode is null ? DBNull.Value : seasonCode);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<OutcomeHeader>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt64(3),
                    ParseTimestamp(reader.GetString(4)),
                    ParseTimestamp(reader.GetString(5)),
                    reader.GetString(6)));
        }
        return results;
    }

    private static async Task<IReadOnlyList<ClaimOutcomeRow>>
        ReadClaimOutcomesAsync(
            SqliteConnection connection,
            OutcomeHeader outcome,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                claim.claim_id,
                claim.source_key,
                claim.available_at_utc,
                claim.deadline_utc,
                claim.start_status,
                claim.forecast_probability,
                claim.claim_content_sha256,
                claim_identity.code,
                player_outcome.starts
            FROM evidence_claims AS claim
            INNER JOIN official_fpl_players AS claim_identity
                ON claim_identity.capture_id = claim.identity_capture_id
               AND claim_identity.player_id = claim.player_id
            LEFT JOIN official_fpl_players AS outcome_identity
                ON outcome_identity.capture_id = $referenceCaptureId
               AND outcome_identity.code = claim_identity.code
            LEFT JOIN official_fpl_player_outcomes AS player_outcome
                ON player_outcome.outcome_capture_id = $outcomeCaptureId
               AND player_outcome.reference_capture_id = $referenceCaptureId
               AND player_outcome.player_id = outcome_identity.player_id
            WHERE claim.season_code = $seasonCode
              AND claim.gameweek = $gameweek
              AND claim.status = 'quarantined'
              AND claim.claim_type = 'start'
              AND julianday(claim.available_at_utc)
                    <= julianday(claim.deadline_utc)
            ORDER BY claim.available_at_utc, claim.claim_id;
            """;
        command.Parameters.AddWithValue(
            "$referenceCaptureId",
            outcome.ReferenceCaptureId);
        command.Parameters.AddWithValue(
            "$outcomeCaptureId",
            outcome.OutcomeCaptureId);
        command.Parameters.AddWithValue("$seasonCode", outcome.SeasonCode);
        command.Parameters.AddWithValue("$gameweek", outcome.Gameweek);
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<ClaimOutcomeRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    ParseTimestamp(reader.GetString(2)),
                    ParseTimestamp(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5)
                        ? null
                        : decimal.Parse(
                            reader.GetString(5),
                            CultureInfo.InvariantCulture),
                    reader.GetString(6),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8)));
        }
        return results;
    }

    private static int CompareFolds(
        EvidenceClaimEvaluationFoldDocument left,
        EvidenceClaimEvaluationFoldDocument right)
    {
        int season = StringComparer.Ordinal.Compare(
            left.SeasonCode,
            right.SeasonCode);
        return season != 0 ? season : left.Gameweek.CompareTo(right.Gameweek);
    }

    private static int CompareExclusions(
        EvidenceClaimEvaluationExclusionDocument left,
        EvidenceClaimEvaluationExclusionDocument right)
    {
        int season = StringComparer.Ordinal.Compare(
            left.SeasonCode,
            right.SeasonCode);
        return season != 0 ? season : left.Gameweek.CompareTo(right.Gameweek);
    }

    private static double Round(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string Hash<T>(T value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private sealed record OutcomeHeader(
        long OutcomeCaptureId,
        string SeasonCode,
        int Gameweek,
        long ReferenceCaptureId,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset AvailableAtUtc,
        string LiveSha256);

    private sealed record ClaimOutcomeRow(
        long ClaimId,
        string SourceKey,
        DateTimeOffset AvailableAtUtc,
        DateTimeOffset DeadlineUtc,
        string? StartStatus,
        decimal? ForecastProbability,
        string ClaimContentSha256,
        int PlayerCode,
        int? Starts);

    private sealed record EvaluationSample(
        string SeasonCode,
        int Gameweek,
        string SourceKey,
        string ClaimType,
        string LeadTimeBucket,
        bool Predicted,
        bool Actual,
        double? Probability);

    private sealed record SliceKey(
        string SourceKey,
        string ClaimType,
        string LeadTimeBucket);

    private sealed record SourceAssertionKey(
        string SourceKey,
        int PlayerCode);
}
