using AutoFpl.Api.Forecasts;
using AutoFpl.Api.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class AnalyticsSnapshotPublisherTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            $"autofpl-analytics-snapshot-{Guid.NewGuid():N}");

    [Fact]
    public async Task Publisher_creates_standalone_snapshot_only_when_source_changes()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_root);
        string sourcePath = Path.Combine(_root, "source", "autofpl.db");
        string snapshotPath =
            Path.Combine(_root, "snapshot", "autofpl.db");
        DatabaseOptions databaseOptions = Options(sourcePath);
        var store = new DecisionSnapshotStore(databaseOptions);
        await store.MigrateAsync(cancellationToken);
        AnalyticsSnapshotOptions snapshotOptions =
            SnapshotOptions(snapshotPath, "1");
        var publisher = new AnalyticsSnapshotPublisher(
            store,
            databaseOptions,
            snapshotOptions,
            TimeProvider.System);

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        Assert.True(File.Exists(snapshotPath));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.GetDirectoryName(snapshotPath)!,
                ".autofpl-snapshot-*.tmp"));
        Assert.Equal(
            "unchanged",
            await publisher.PublishOnceAsync(cancellationToken));
        var readOnly = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        await using (var source =
            new SqliteConnection(databaseOptions.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using SqliteCommand archive = source.CreateCommand();
            archive.CommandText =
                """
                INSERT INTO historical_fpl_season_captures (
                    capture_id, schema_version, source_key, season_code,
                    source_revision, players_url, gameweeks_url,
                    published_at_utc, retrieved_at_utc, available_at_utc,
                    players_sha256, gameweeks_sha256, players_csv_brotli,
                    gameweeks_csv_brotli, player_count,
                    player_gameweek_count, stable_code_count, created_at_utc
                )
                VALUES (
                    1, '1.0', 'vaastav-fpl-historical/v1', '2022-23',
                    $revision, 'https://example.test/players.csv',
                    'https://example.test/gameweeks.csv',
                    '2026-06-17T12:19:44Z',
                    '2026-07-29T12:00:00Z',
                    '2026-07-29T12:00:00Z',
                    $playersHash, $gameweeksHash, X'01', X'01',
                    1, 1, 1, '2026-07-29T12:00:00Z'
                );
                """;
            archive.Parameters.AddWithValue(
                "$revision",
                new string('d', 40));
            archive.Parameters.AddWithValue(
                "$playersHash",
                new string('e', 64));
            archive.Parameters.AddWithValue(
                "$gameweeksHash",
                new string('f', 64));
            await archive.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        await using (var archiveSnapshot =
            new SqliteConnection(readOnly.ToString()))
        {
            await archiveSnapshot.OpenAsync(cancellationToken);
            await using SqliteCommand count =
                archiveSnapshot.CreateCommand();
            count.CommandText =
                "SELECT COUNT(*) FROM historical_fpl_season_captures;";
            Assert.Equal(
                1L,
                await count.ExecuteScalarAsync(cancellationToken));
        }
        Assert.Equal(
            "unchanged",
            await publisher.PublishOnceAsync(cancellationToken));

        await using (var source =
            new SqliteConnection(databaseOptions.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using SqliteCommand outcome = source.CreateCommand();
            outcome.CommandText =
                """
                INSERT INTO official_fpl_captures (
                    capture_id, schema_version, source_key, season_code,
                    bootstrap_url, fixtures_url, retrieved_at_utc,
                    available_at_utc, bootstrap_sha256, fixtures_sha256,
                    bootstrap_json, fixtures_json, event_count, team_count,
                    player_count, fixture_count, next_gameweek_number,
                    next_deadline_utc, latest_completed_gameweek,
                    created_at_utc
                )
                VALUES (
                    1, '1.0', 'official-fpl-api/v1', '2026-27',
                    'https://example.test/bootstrap',
                    'https://example.test/fixtures',
                    '2026-08-22T12:00:00Z',
                    '2026-08-22T12:00:00Z', $bootstrapHash,
                    $fixturesHash, X'7B7D', X'5B5D', 38, 20, 600, 380,
                    2, '2026-08-28T17:30:00Z', 1,
                    '2026-08-22T12:00:00Z'
                );
                INSERT INTO official_fpl_outcome_captures (
                    outcome_capture_id, schema_version, source_key,
                    season_code, gameweek, reference_capture_id, live_url,
                    retrieved_at_utc, available_at_utc, live_sha256,
                    live_json, player_count, gameweek_fixture_count,
                    created_at_utc
                )
                VALUES (
                    1, '1.0', 'official-fpl-api-event-live/v1',
                    '2026-27', 1, 1, 'https://example.test/event/1/live',
                    '2026-08-22T12:00:00Z',
                    '2026-08-22T12:00:00Z', $liveHash, X'7B7D',
                    600, 10, '2026-08-22T12:00:00Z'
                );
                """;
            outcome.Parameters.AddWithValue(
                "$bootstrapHash",
                new string('a', 64));
            outcome.Parameters.AddWithValue(
                "$fixturesHash",
                new string('b', 64));
            outcome.Parameters.AddWithValue(
                "$liveHash",
                new string('c', 64));
            await outcome.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        await using (var outcomeSnapshot =
            new SqliteConnection(readOnly.ToString()))
        {
            await outcomeSnapshot.OpenAsync(cancellationToken);
            await using SqliteCommand count =
                outcomeSnapshot.CreateCommand();
            count.CommandText =
                "SELECT COUNT(*) FROM official_fpl_outcome_captures;";
            Assert.Equal(
                1L,
                await count.ExecuteScalarAsync(cancellationToken));
        }
        Assert.Equal(
            "unchanged",
            await publisher.PublishOnceAsync(cancellationToken));

        await using (var source =
            new SqliteConnection(databaseOptions.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using SqliteCommand selected = source.CreateCommand();
            selected.CommandText =
                """
                INSERT INTO selected_opening_squad_shadow_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    official_capture_id, season_code, opening_gameweek,
                    decision_cutoff_utc, evaluation_policy_key,
                    evaluation_data_identity_sha256, scenario_count,
                    candidate_pool_count, budget_tenths,
                    producer_data_identity_sha256,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    '1.0', 'current-selected-opening-squad-shadow',
                    'current-selected-opening-squad-shadow-v1',
                    'prospective-shadow-unscored', 1, '2026-27', 1,
                    '2026-07-29T12:00:00Z', '6-expected-points',
                    $evaluationIdentity, 38, 600, 980,
                    $dataIdentity, $runIdentity, '{}', $contentHash,
                    '2026-07-29T18:00:00Z'
                );
                """;
            selected.Parameters.AddWithValue(
                "$evaluationIdentity",
                SelectedOpeningSquadShadowStore.EvaluationDataIdentity);
            selected.Parameters.AddWithValue(
                "$dataIdentity",
                new string('1', 64));
            selected.Parameters.AddWithValue(
                "$runIdentity",
                new string('2', 64));
            selected.Parameters.AddWithValue(
                "$contentHash",
                new string('3', 64));
            await selected.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        await using (var selectedSnapshot =
            new SqliteConnection(readOnly.ToString()))
        {
            await selectedSnapshot.OpenAsync(cancellationToken);
            await using SqliteCommand count =
                selectedSnapshot.CreateCommand();
            count.CommandText =
                "SELECT COUNT(*) "
                + "FROM selected_opening_squad_shadow_artifacts;";
            Assert.Equal(
                1L,
                await count.ExecuteScalarAsync(cancellationToken));
        }
        Assert.Equal(
            "unchanged",
            await publisher.PublishOnceAsync(cancellationToken));

        await using (var source =
            new SqliteConnection(databaseOptions.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using SqliteCommand official = source.CreateCommand();
            official.CommandText =
                """
                INSERT INTO official_published_opening_squad_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    official_capture_id, source_bootstrap_sha256,
                    source_fixtures_sha256, incumbent_run_identity_sha256,
                    producer_data_identity_sha256,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    '1.0',
                    'current-official-published-opening-squad-shadow',
                    'current-official-published-opening-squad-shadow-v1',
                    'prospective-official-baseline-unscored', 1,
                    $bootstrapHash, $fixturesHash, $incumbentIdentity,
                    $dataIdentity, $runIdentity, '{}', $contentHash,
                    '2026-07-29T18:01:00Z'
                );
                """;
            official.Parameters.AddWithValue(
                "$bootstrapHash",
                new string('a', 64));
            official.Parameters.AddWithValue(
                "$fixturesHash",
                new string('b', 64));
            official.Parameters.AddWithValue(
                "$incumbentIdentity",
                new string('2', 64));
            official.Parameters.AddWithValue(
                "$dataIdentity",
                new string('4', 64));
            official.Parameters.AddWithValue(
                "$runIdentity",
                new string('5', 64));
            official.Parameters.AddWithValue(
                "$contentHash",
                new string('6', 64));
            await official.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        await using (var officialSnapshot =
            new SqliteConnection(readOnly.ToString()))
        {
            await officialSnapshot.OpenAsync(cancellationToken);
            await using SqliteCommand count =
                officialSnapshot.CreateCommand();
            count.CommandText =
                "SELECT COUNT(*) "
                + "FROM official_published_opening_squad_artifacts;";
            Assert.Equal(
                1L,
                await count.ExecuteScalarAsync(cancellationToken));
        }
        Assert.Equal(
            "unchanged",
            await publisher.PublishOnceAsync(cancellationToken));

        await using (var source =
            new SqliteConnection(databaseOptions.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using SqliteCommand evidence = source.CreateCommand();
            evidence.CommandText =
                """
                INSERT INTO official_fpl_teams (
                    capture_id, team_id, code, name, short_name
                )
                VALUES (1, 1, 10, 'Team 1', 'T1');

                INSERT INTO official_fpl_players (
                    capture_id, player_id, code, team_id, position,
                    first_name, second_name, web_name, price_tenths, status,
                    news, news_added_utc, chance_next_round,
                    selected_by_percent, total_points, minutes, starts,
                    photo_identifier, expected_points_next
                )
                VALUES (
                    1, 1, 10001, 1, 'defender', 'Player', 'One',
                    'Player 1', 50, 'a', '', NULL, NULL, '1.0',
                    0, 0, 0, '1.jpg', NULL
                );

                INSERT INTO evidence_claims (
                    schema_version, status, source_key, canonical_url, author,
                    published_at_utc, retrieved_at_utc, available_at_utc,
                    content_sha256, source_revision, season_code, gameweek,
                    deadline_utc, player_id, identity_capture_id, claim_type,
                    availability_status, start_status, forecast_probability,
                    expected_minutes, role, directness, source_span,
                    extraction_method, extraction_version,
                    extraction_confidence, duplicate_cluster_key,
                    claim_content_sha256, created_at_utc
                )
                VALUES (
                    '1.0', 'quarantined', 'ffscout-predicted-lineups',
                    'https://example.test/lineups', 'FFScout', NULL,
                    '2026-07-30T12:00:00Z', '2026-07-30T12:00:00Z',
                    $evidenceHash, 1, '2026-27', 1,
                    '2026-08-28T17:30:00Z', 1, 1, 'start', NULL,
                    'does-not-start', NULL, NULL, NULL, 'reported',
                    'Player 1 omitted', 'deterministic', 'test/v1', '1',
                    $clusterHash, $claimHash, '2026-07-30T12:00:00Z'
                );
                """;
            evidence.Parameters.AddWithValue(
                "$evidenceHash",
                new string('4', 64));
            evidence.Parameters.AddWithValue(
                "$clusterHash",
                new string('5', 64));
            evidence.Parameters.AddWithValue(
                "$claimHash",
                new string('6', 64));
            await evidence.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        await using (var evidenceSnapshot =
            new SqliteConnection(readOnly.ToString()))
        {
            await evidenceSnapshot.OpenAsync(cancellationToken);
            await using SqliteCommand count =
                evidenceSnapshot.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM evidence_claims;";
            Assert.Equal(
                1L,
                await count.ExecuteScalarAsync(cancellationToken));
        }
        Assert.Equal(
            "unchanged",
            await publisher.PublishOnceAsync(cancellationToken));

        await using (var source =
            new SqliteConnection(databaseOptions.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using SqliteCommand stress = source.CreateCommand();
            stress.CommandText =
                """
                INSERT INTO external_evidence_stress_artifacts (
                    schema_version, artifact_type, artifact_version, status,
                    official_capture_id, season_code, opening_gameweek,
                    evidence_cutoff_utc, producer_data_identity_sha256,
                    producer_run_identity_sha256, document_json,
                    content_sha256, created_at_utc
                )
                VALUES (
                    '1.0', 'current-external-evidence-stress',
                    'current-external-evidence-stress-v1',
                    'external-evidence-stress-non-serving',
                    1, '2026-27', 1, '2026-07-30T12:00:00Z',
                    $dataIdentity, $runIdentity, '{}', $contentHash,
                    '2026-07-30T12:01:00Z'
                );
                """;
            stress.Parameters.AddWithValue(
                "$dataIdentity",
                new string('7', 64));
            stress.Parameters.AddWithValue(
                "$runIdentity",
                new string('8', 64));
            stress.Parameters.AddWithValue(
                "$contentHash",
                new string('9', 64));
            await stress.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        await using (var stressSnapshot =
            new SqliteConnection(readOnly.ToString()))
        {
            await stressSnapshot.OpenAsync(cancellationToken);
            await using SqliteCommand count =
                stressSnapshot.CreateCommand();
            count.CommandText =
                "SELECT COUNT(*) FROM external_evidence_stress_artifacts;";
            Assert.Equal(
                1L,
                await count.ExecuteScalarAsync(cancellationToken));
        }
        Assert.Equal(
            "unchanged",
            await publisher.PublishOnceAsync(cancellationToken));

        await using (var snapshot =
            new SqliteConnection(readOnly.ToString()))
        {
            await snapshot.OpenAsync(cancellationToken);
            await using SqliteCommand journal = snapshot.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode;";
            Assert.Equal(
                "delete",
                Assert.IsType<string>(
                    await journal.ExecuteScalarAsync(
                        cancellationToken)),
                ignoreCase: true);
            await using SqliteCommand integrity =
                snapshot.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            Assert.Equal(
                "ok",
                Assert.IsType<string>(
                    await integrity.ExecuteScalarAsync(
                        cancellationToken)));
        }

        await using (var source =
            new SqliteConnection(databaseOptions.ConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await using SqliteCommand change = source.CreateCommand();
            change.CommandText =
                """
                INSERT INTO schema_migrations (
                    version, name, applied_at_utc
                )
                VALUES (
                    999, 'test-change', '2026-07-29T10:00:00Z'
                );
                """;
            await change.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(
            "published",
            await publisher.PublishOnceAsync(cancellationToken));
        await using var updated =
            new SqliteConnection(readOnly.ToString());
        await updated.OpenAsync(cancellationToken);
        await using SqliteCommand latest = updated.CreateCommand();
        latest.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal(
            999L,
            await latest.ExecuteScalarAsync(cancellationToken));
    }

    [Fact]
    public void Options_are_disabled_without_interval_and_validate_path()
    {
        AnalyticsSnapshotOptions disabled =
            AnalyticsSnapshotOptions.FromConfiguration(
                new ConfigurationBuilder().Build());
        Assert.False(disabled.Enabled);

        IConfiguration invalid =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [
                            "AutoFpl:Analytics:"
                            + "SnapshotPollIntervalMinutes"
                        ] = "0",
                        ["AutoFpl:Analytics:SnapshotPath"] =
                            Path.Combine(_root, "snapshot.db"),
                    })
                .Build();
        Assert.Throws<InvalidOperationException>(
            () => AnalyticsSnapshotOptions.FromConfiguration(invalid));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static DatabaseOptions Options(string databasePath)
    {
        IConfiguration configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["AutoFpl:DatabasePath"] = databasePath,
                    })
                .Build();
        return DatabaseOptions.FromConfiguration(configuration);
    }

    private static AnalyticsSnapshotOptions SnapshotOptions(
        string path,
        string interval)
    {
        IConfiguration configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [
                            "AutoFpl:Analytics:"
                            + "SnapshotPollIntervalMinutes"
                        ] = interval,
                        ["AutoFpl:Analytics:SnapshotPath"] = path,
                    })
                .Build();
        return AnalyticsSnapshotOptions.FromConfiguration(configuration);
    }
}
