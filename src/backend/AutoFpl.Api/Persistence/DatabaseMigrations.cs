namespace AutoFpl.Api.Persistence;

internal sealed record DatabaseMigration(int Version, string Name, string Sql);

internal static class DatabaseMigrations
{
    public const int CurrentVersion = 27;

    public static IReadOnlyList<DatabaseMigration> All { get; } =
    [
        new(
            1,
            "season-gameweek-player",
            """
            CREATE TABLE seasons (
                season_id INTEGER PRIMARY KEY,
                code TEXT NOT NULL UNIQUE
                    CHECK (length(code) BETWEEN 4 AND 16)
            );

            CREATE TABLE gameweeks (
                gameweek_id INTEGER PRIMARY KEY,
                season_id INTEGER NOT NULL REFERENCES seasons(season_id),
                number INTEGER NOT NULL CHECK (number BETWEEN 1 AND 38),
                deadline_utc TEXT NOT NULL,
                UNIQUE (season_id, number)
            );

            CREATE TABLE players (
                player_id INTEGER PRIMARY KEY CHECK (player_id > 0),
                created_at_utc TEXT NOT NULL
            );
            """),
        new(
            2,
            "squad-selection",
            """
            CREATE TABLE squads (
                squad_id INTEGER PRIMARY KEY,
                gameweek_id INTEGER NOT NULL REFERENCES gameweeks(gameweek_id),
                budget_tenths INTEGER NOT NULL CHECK (budget_tenths > 0),
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE squad_players (
                squad_id INTEGER NOT NULL REFERENCES squads(squad_id) ON DELETE RESTRICT,
                player_id INTEGER NOT NULL REFERENCES players(player_id) ON DELETE RESTRICT,
                squad_order INTEGER NOT NULL CHECK (squad_order BETWEEN 1 AND 15),
                display_name TEXT NOT NULL CHECK (length(display_name) BETWEEN 1 AND 100),
                club_id INTEGER NOT NULL CHECK (club_id > 0),
                position TEXT NOT NULL
                    CHECK (position IN ('goalkeeper', 'defender', 'midfielder', 'forward')),
                price_tenths INTEGER NOT NULL CHECK (price_tenths > 0),
                PRIMARY KEY (squad_id, player_id),
                UNIQUE (squad_id, squad_order)
            );

            CREATE TABLE selections (
                selection_id INTEGER PRIMARY KEY,
                squad_id INTEGER NOT NULL UNIQUE REFERENCES squads(squad_id) ON DELETE RESTRICT,
                captain_player_id INTEGER NOT NULL,
                vice_captain_player_id INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                UNIQUE (selection_id, squad_id),
                FOREIGN KEY (squad_id, captain_player_id)
                    REFERENCES squad_players(squad_id, player_id),
                FOREIGN KEY (squad_id, vice_captain_player_id)
                    REFERENCES squad_players(squad_id, player_id)
            );

            CREATE TABLE selection_players (
                selection_id INTEGER NOT NULL,
                squad_id INTEGER NOT NULL,
                player_id INTEGER NOT NULL,
                role TEXT NOT NULL
                    CHECK (role IN ('starter', 'replacement-goalkeeper', 'outfield-substitute')),
                bench_order INTEGER,
                PRIMARY KEY (selection_id, player_id),
                FOREIGN KEY (selection_id, squad_id)
                    REFERENCES selections(selection_id, squad_id) ON DELETE RESTRICT,
                FOREIGN KEY (squad_id, player_id)
                    REFERENCES squad_players(squad_id, player_id) ON DELETE RESTRICT,
                CHECK (
                    (role = 'starter' AND bench_order IS NULL)
                    OR (role = 'replacement-goalkeeper' AND bench_order = 1)
                    OR (role = 'outfield-substitute' AND bench_order BETWEEN 2 AND 4)
                )
            );
            """),
        new(
            3,
            "observation-snapshot",
            """
            CREATE TABLE source_observations (
                observation_id INTEGER PRIMARY KEY,
                gameweek_id INTEGER NOT NULL REFERENCES gameweeks(gameweek_id),
                player_id INTEGER NOT NULL REFERENCES players(player_id),
                source_key TEXT NOT NULL CHECK (length(source_key) BETWEEN 1 AND 100),
                metric TEXT NOT NULL CHECK (length(metric) BETWEEN 1 AND 64),
                value_decimal TEXT NOT NULL,
                revision INTEGER NOT NULL CHECK (revision > 0),
                supersedes_observation_id INTEGER UNIQUE
                    REFERENCES source_observations(observation_id) ON DELETE RESTRICT,
                observed_at_utc TEXT NOT NULL,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                UNIQUE (gameweek_id, player_id, source_key, metric, revision)
            );

            CREATE INDEX source_observations_cutoff_idx
                ON source_observations (
                    gameweek_id,
                    player_id,
                    source_key,
                    metric,
                    available_at_utc,
                    revision
                );

            CREATE TABLE decision_snapshots (
                snapshot_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                revision INTEGER NOT NULL CHECK (revision > 0),
                supersedes_snapshot_id INTEGER UNIQUE
                    REFERENCES decision_snapshots(snapshot_id) ON DELETE RESTRICT,
                gameweek_id INTEGER NOT NULL REFERENCES gameweeks(gameweek_id),
                squad_id INTEGER NOT NULL REFERENCES squads(squad_id),
                selection_id INTEGER NOT NULL REFERENCES selections(selection_id),
                season_code TEXT NOT NULL,
                gameweek_number INTEGER NOT NULL CHECK (gameweek_number BETWEEN 1 AND 38),
                deadline_utc TEXT NOT NULL,
                decision_cutoff_utc TEXT NOT NULL,
                content_hash TEXT NOT NULL CHECK (length(content_hash) = 64),
                created_at_utc TEXT NOT NULL,
                UNIQUE (gameweek_id, revision),
                FOREIGN KEY (selection_id, squad_id)
                    REFERENCES selections(selection_id, squad_id) ON DELETE RESTRICT
            );

            CREATE TABLE decision_snapshot_observations (
                snapshot_id INTEGER NOT NULL
                    REFERENCES decision_snapshots(snapshot_id) ON DELETE RESTRICT,
                observation_id INTEGER NOT NULL
                    REFERENCES source_observations(observation_id) ON DELETE RESTRICT,
                PRIMARY KEY (snapshot_id, observation_id)
            );
            """),
        new(
            4,
            "official-fpl-capture",
            """
            CREATE TABLE official_fpl_captures (
                capture_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                source_key TEXT NOT NULL CHECK (source_key = 'official-fpl-api/v1'),
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                bootstrap_url TEXT NOT NULL,
                fixtures_url TEXT NOT NULL,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                bootstrap_sha256 TEXT NOT NULL CHECK (length(bootstrap_sha256) = 64),
                fixtures_sha256 TEXT NOT NULL CHECK (length(fixtures_sha256) = 64),
                bootstrap_json BLOB NOT NULL,
                fixtures_json BLOB NOT NULL,
                event_count INTEGER NOT NULL CHECK (event_count BETWEEN 1 AND 38),
                team_count INTEGER NOT NULL CHECK (team_count BETWEEN 1 AND 40),
                player_count INTEGER NOT NULL CHECK (player_count BETWEEN 1 AND 2000),
                fixture_count INTEGER NOT NULL CHECK (fixture_count BETWEEN 0 AND 1000),
                next_gameweek_number INTEGER CHECK (next_gameweek_number BETWEEN 1 AND 38),
                next_deadline_utc TEXT,
                latest_completed_gameweek INTEGER
                    CHECK (latest_completed_gameweek BETWEEN 1 AND 38),
                created_at_utc TEXT NOT NULL,
                UNIQUE (bootstrap_sha256, fixtures_sha256)
            );

            CREATE INDEX official_fpl_captures_latest_idx
                ON official_fpl_captures (available_at_utc DESC, capture_id DESC);

            CREATE TABLE official_fpl_events (
                capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id) ON DELETE RESTRICT,
                event_id INTEGER NOT NULL CHECK (event_id BETWEEN 1 AND 38),
                name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 100),
                deadline_utc TEXT NOT NULL,
                finished INTEGER NOT NULL CHECK (finished IN (0, 1)),
                data_checked INTEGER NOT NULL CHECK (data_checked IN (0, 1)),
                is_current INTEGER NOT NULL CHECK (is_current IN (0, 1)),
                is_next INTEGER NOT NULL CHECK (is_next IN (0, 1)),
                PRIMARY KEY (capture_id, event_id)
            );

            CREATE TABLE official_fpl_teams (
                capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id) ON DELETE RESTRICT,
                team_id INTEGER NOT NULL CHECK (team_id > 0),
                code INTEGER NOT NULL CHECK (code > 0),
                name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 100),
                short_name TEXT NOT NULL CHECK (length(short_name) BETWEEN 1 AND 8),
                PRIMARY KEY (capture_id, team_id)
            );

            CREATE TABLE official_fpl_players (
                capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id) ON DELETE RESTRICT,
                player_id INTEGER NOT NULL CHECK (player_id > 0),
                code INTEGER NOT NULL CHECK (code > 0),
                team_id INTEGER NOT NULL,
                position TEXT NOT NULL
                    CHECK (position IN ('goalkeeper', 'defender', 'midfielder', 'forward')),
                first_name TEXT NOT NULL CHECK (length(first_name) BETWEEN 1 AND 100),
                second_name TEXT NOT NULL CHECK (length(second_name) BETWEEN 1 AND 100),
                web_name TEXT NOT NULL CHECK (length(web_name) BETWEEN 1 AND 100),
                price_tenths INTEGER NOT NULL CHECK (price_tenths > 0),
                status TEXT NOT NULL CHECK (length(status) BETWEEN 1 AND 8),
                news TEXT NOT NULL,
                news_added_utc TEXT,
                chance_next_round INTEGER
                    CHECK (chance_next_round BETWEEN 0 AND 100),
                selected_by_percent TEXT NOT NULL,
                total_points INTEGER NOT NULL,
                minutes INTEGER NOT NULL CHECK (minutes >= 0),
                starts INTEGER NOT NULL CHECK (starts >= 0),
                PRIMARY KEY (capture_id, player_id),
                FOREIGN KEY (capture_id, team_id)
                    REFERENCES official_fpl_teams(capture_id, team_id) ON DELETE RESTRICT
            );

            CREATE TABLE official_fpl_fixtures (
                capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id) ON DELETE RESTRICT,
                fixture_id INTEGER NOT NULL CHECK (fixture_id > 0),
                event_id INTEGER CHECK (event_id BETWEEN 1 AND 38),
                home_team_id INTEGER NOT NULL,
                away_team_id INTEGER NOT NULL,
                kickoff_utc TEXT,
                started INTEGER NOT NULL CHECK (started IN (0, 1)),
                finished INTEGER NOT NULL CHECK (finished IN (0, 1)),
                finished_provisional INTEGER NOT NULL CHECK (finished_provisional IN (0, 1)),
                home_score INTEGER,
                away_score INTEGER,
                PRIMARY KEY (capture_id, fixture_id),
                FOREIGN KEY (capture_id, event_id)
                    REFERENCES official_fpl_events(capture_id, event_id) ON DELETE RESTRICT,
                FOREIGN KEY (capture_id, home_team_id)
                    REFERENCES official_fpl_teams(capture_id, team_id) ON DELETE RESTRICT,
                FOREIGN KEY (capture_id, away_team_id)
                    REFERENCES official_fpl_teams(capture_id, team_id) ON DELETE RESTRICT,
                CHECK (home_team_id <> away_team_id),
                CHECK (
                    (home_score IS NULL AND away_score IS NULL)
                    OR (home_score >= 0 AND away_score >= 0)
                )
            );
            """),
        new(
            5,
            "official-fpl-gameweek-outcome",
            """
            CREATE TABLE official_fpl_outcome_captures (
                outcome_capture_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                source_key TEXT NOT NULL
                    CHECK (source_key = 'official-fpl-api-event-live/v1'),
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                reference_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id) ON DELETE RESTRICT,
                live_url TEXT NOT NULL,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                live_sha256 TEXT NOT NULL CHECK (length(live_sha256) = 64),
                live_json BLOB NOT NULL,
                player_count INTEGER NOT NULL CHECK (player_count BETWEEN 1 AND 2000),
                gameweek_fixture_count INTEGER NOT NULL
                    CHECK (gameweek_fixture_count BETWEEN 1 AND 100),
                created_at_utc TEXT NOT NULL,
                UNIQUE (season_code, gameweek, live_sha256),
                UNIQUE (outcome_capture_id, reference_capture_id)
            );

            CREATE INDEX official_fpl_outcome_captures_latest_idx
                ON official_fpl_outcome_captures (
                    season_code,
                    gameweek,
                    available_at_utc DESC,
                    outcome_capture_id DESC
                );

            CREATE TABLE official_fpl_player_outcomes (
                outcome_capture_id INTEGER NOT NULL,
                reference_capture_id INTEGER NOT NULL,
                player_id INTEGER NOT NULL CHECK (player_id > 0),
                minutes INTEGER NOT NULL CHECK (minutes BETWEEN 0 AND 400),
                starts INTEGER NOT NULL CHECK (starts BETWEEN 0 AND 4),
                total_points INTEGER NOT NULL CHECK (total_points BETWEEN -100 AND 500),
                goals_scored INTEGER NOT NULL CHECK (goals_scored BETWEEN 0 AND 20),
                assists INTEGER NOT NULL CHECK (assists BETWEEN 0 AND 20),
                clean_sheets INTEGER NOT NULL CHECK (clean_sheets BETWEEN 0 AND 4),
                goals_conceded INTEGER NOT NULL CHECK (goals_conceded BETWEEN 0 AND 50),
                saves INTEGER NOT NULL CHECK (saves BETWEEN 0 AND 100),
                bonus INTEGER NOT NULL CHECK (bonus BETWEEN 0 AND 30),
                yellow_cards INTEGER NOT NULL CHECK (yellow_cards BETWEEN 0 AND 4),
                red_cards INTEGER NOT NULL CHECK (red_cards BETWEEN 0 AND 4),
                PRIMARY KEY (outcome_capture_id, player_id),
                FOREIGN KEY (outcome_capture_id, reference_capture_id)
                    REFERENCES official_fpl_outcome_captures(
                        outcome_capture_id,
                        reference_capture_id
                    ) ON DELETE RESTRICT,
                FOREIGN KEY (reference_capture_id, player_id)
                    REFERENCES official_fpl_players(
                        capture_id,
                        player_id
                    ) ON DELETE RESTRICT
            );
            """),
        new(
            6,
            "fpl-form-forecast-capture",
            """
            CREATE TABLE fpl_form_forecast_captures (
                capture_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                source_key TEXT NOT NULL
                    CHECK (source_key = 'fpl-form-public-forecast/v1'),
                source_url TEXT NOT NULL
                    CHECK (
                        source_url =
                        'https://www.fplform.com/fpl-predicted-points.php'
                    ),
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                content_sha256 TEXT NOT NULL CHECK (length(content_sha256) = 64),
                html_brotli BLOB NOT NULL,
                player_count INTEGER NOT NULL CHECK (player_count BETWEEN 1 AND 2000),
                fixture_prediction_count INTEGER NOT NULL
                    CHECK (fixture_prediction_count BETWEEN 1 AND 4000),
                appearance_probability_count INTEGER NOT NULL
                    CHECK (
                        appearance_probability_count BETWEEN 0
                        AND fixture_prediction_count
                    ),
                created_at_utc TEXT NOT NULL,
                UNIQUE (content_sha256),
                UNIQUE (capture_id, gameweek)
            );

            CREATE INDEX fpl_form_forecast_captures_latest_idx
                ON fpl_form_forecast_captures (
                    season_code,
                    gameweek,
                    available_at_utc DESC,
                    capture_id DESC
                );

            CREATE TABLE fpl_form_fixture_predictions (
                capture_id INTEGER NOT NULL,
                source_player_id INTEGER NOT NULL CHECK (source_player_id > 0),
                fixture_id INTEGER NOT NULL CHECK (fixture_id > 0),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                player_name TEXT NOT NULL CHECK (length(player_name) BETWEEN 1 AND 150),
                team_name TEXT NOT NULL CHECK (length(team_name) BETWEEN 1 AND 100),
                position TEXT NOT NULL
                    CHECK (
                        position IN (
                            'goalkeeper',
                            'defender',
                            'midfielder',
                            'forward'
                        )
                    ),
                kickoff_local TEXT NOT NULL
                    CHECK (length(kickoff_local) BETWEEN 1 AND 32),
                predicted_points TEXT NOT NULL,
                appearance_probability TEXT,
                PRIMARY KEY (capture_id, source_player_id, fixture_id),
                FOREIGN KEY (capture_id, gameweek)
                    REFERENCES fpl_form_forecast_captures(
                        capture_id,
                        gameweek
                    ) ON DELETE RESTRICT
            );
            """),
        new(
            7,
            "official-fpl-player-photo",
            """
            ALTER TABLE official_fpl_players
                ADD COLUMN photo_identifier TEXT
                    CHECK (
                        photo_identifier IS NULL
                        OR (
                            length(photo_identifier) BETWEEN 5 AND 100
                            AND (
                                photo_identifier GLOB '[0-9]*.jpg'
                                OR photo_identifier GLOB '[0-9]*.png'
                            )
                        )
                    );

            CREATE INDEX official_fpl_players_code_idx
                ON official_fpl_players (capture_id, code);
            """),
        new(
            8,
            "fpl-form-playwright-evidence",
            """
            CREATE TABLE fpl_form_forecast_captures_v2 (
                capture_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL
                    CHECK (schema_version IN ('1.0', '1.1')),
                source_key TEXT NOT NULL
                    CHECK (source_key = 'fpl-form-public-forecast/v1'),
                source_url TEXT NOT NULL
                    CHECK (
                        source_url =
                        'https://www.fplform.com/fpl-predicted-points.php'
                    ),
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                content_sha256 TEXT NOT NULL CHECK (length(content_sha256) = 64),
                transport TEXT NOT NULL
                    CHECK (
                        transport IN (
                            'direct-http/v1',
                            'playwright-mcp/v1'
                        )
                    ),
                extraction_version TEXT NOT NULL
                    CHECK (length(extraction_version) BETWEEN 1 AND 100),
                provider_payload_sha256 TEXT
                    CHECK (
                        provider_payload_sha256 IS NULL
                        OR length(provider_payload_sha256) = 64
                    ),
                evidence_brotli BLOB NOT NULL,
                player_count INTEGER NOT NULL CHECK (player_count BETWEEN 1 AND 2000),
                fixture_prediction_count INTEGER NOT NULL
                    CHECK (fixture_prediction_count BETWEEN 1 AND 4000),
                appearance_probability_count INTEGER NOT NULL
                    CHECK (
                        appearance_probability_count BETWEEN 0
                        AND fixture_prediction_count
                    ),
                created_at_utc TEXT NOT NULL,
                UNIQUE (content_sha256),
                UNIQUE (capture_id, gameweek)
            );

            CREATE TABLE fpl_form_fixture_predictions_v2 (
                capture_id INTEGER NOT NULL,
                source_player_id INTEGER NOT NULL CHECK (source_player_id > 0),
                fixture_id INTEGER NOT NULL CHECK (fixture_id > 0),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                player_name TEXT NOT NULL CHECK (length(player_name) BETWEEN 1 AND 150),
                team_name TEXT NOT NULL CHECK (length(team_name) BETWEEN 1 AND 100),
                position TEXT NOT NULL
                    CHECK (
                        position IN (
                            'goalkeeper',
                            'defender',
                            'midfielder',
                            'forward'
                        )
                    ),
                kickoff_local TEXT NOT NULL
                    CHECK (length(kickoff_local) BETWEEN 1 AND 32),
                predicted_points TEXT NOT NULL,
                appearance_probability TEXT,
                PRIMARY KEY (capture_id, source_player_id, fixture_id),
                FOREIGN KEY (capture_id, gameweek)
                    REFERENCES fpl_form_forecast_captures_v2(
                        capture_id,
                        gameweek
                    ) ON DELETE RESTRICT
            );

            INSERT INTO fpl_form_forecast_captures_v2 (
                capture_id,
                schema_version,
                source_key,
                source_url,
                season_code,
                gameweek,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                transport,
                extraction_version,
                provider_payload_sha256,
                evidence_brotli,
                player_count,
                fixture_prediction_count,
                appearance_probability_count,
                created_at_utc
            )
            SELECT
                capture_id,
                schema_version,
                source_key,
                source_url,
                season_code,
                gameweek,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                'direct-http/v1',
                'fpl-form-full-html/v1',
                NULL,
                html_brotli,
                player_count,
                fixture_prediction_count,
                appearance_probability_count,
                created_at_utc
            FROM fpl_form_forecast_captures;

            INSERT INTO fpl_form_fixture_predictions_v2 (
                capture_id,
                source_player_id,
                fixture_id,
                gameweek,
                player_name,
                team_name,
                position,
                kickoff_local,
                predicted_points,
                appearance_probability
            )
            SELECT
                capture_id,
                source_player_id,
                fixture_id,
                gameweek,
                player_name,
                team_name,
                position,
                kickoff_local,
                predicted_points,
                appearance_probability
            FROM fpl_form_fixture_predictions;

            DROP TABLE fpl_form_fixture_predictions;
            DROP TABLE fpl_form_forecast_captures;

            ALTER TABLE fpl_form_forecast_captures_v2
                RENAME TO fpl_form_forecast_captures;
            ALTER TABLE fpl_form_fixture_predictions_v2
                RENAME TO fpl_form_fixture_predictions;

            CREATE INDEX fpl_form_forecast_captures_latest_idx
                ON fpl_form_forecast_captures (
                    season_code,
                    gameweek,
                    available_at_utc DESC,
                    capture_id DESC
                );
            """),
        new(
            9,
            "fpl-form-canonical-url",
            """
            CREATE TABLE fpl_form_forecast_captures_v3 (
                capture_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL
                    CHECK (schema_version IN ('1.0', '1.1')),
                source_key TEXT NOT NULL
                    CHECK (source_key = 'fpl-form-public-forecast/v1'),
                source_url TEXT NOT NULL
                    CHECK (
                        source_url IN (
                            'https://www.fplform.com/fpl-predicted-points.php',
                            'https://fplform.com/fpl-predicted-points'
                        )
                    ),
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                content_sha256 TEXT NOT NULL CHECK (length(content_sha256) = 64),
                transport TEXT NOT NULL
                    CHECK (
                        transport IN (
                            'direct-http/v1',
                            'playwright-mcp/v1'
                        )
                    ),
                extraction_version TEXT NOT NULL
                    CHECK (length(extraction_version) BETWEEN 1 AND 100),
                provider_payload_sha256 TEXT
                    CHECK (
                        provider_payload_sha256 IS NULL
                        OR length(provider_payload_sha256) = 64
                    ),
                evidence_brotli BLOB NOT NULL,
                player_count INTEGER NOT NULL CHECK (player_count BETWEEN 1 AND 2000),
                fixture_prediction_count INTEGER NOT NULL
                    CHECK (fixture_prediction_count BETWEEN 1 AND 4000),
                appearance_probability_count INTEGER NOT NULL
                    CHECK (
                        appearance_probability_count BETWEEN 0
                        AND fixture_prediction_count
                    ),
                created_at_utc TEXT NOT NULL,
                UNIQUE (content_sha256),
                UNIQUE (capture_id, gameweek)
            );

            CREATE TABLE fpl_form_fixture_predictions_v3 (
                capture_id INTEGER NOT NULL,
                source_player_id INTEGER NOT NULL CHECK (source_player_id > 0),
                fixture_id INTEGER NOT NULL CHECK (fixture_id > 0),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                player_name TEXT NOT NULL CHECK (length(player_name) BETWEEN 1 AND 150),
                team_name TEXT NOT NULL CHECK (length(team_name) BETWEEN 1 AND 100),
                position TEXT NOT NULL
                    CHECK (
                        position IN (
                            'goalkeeper',
                            'defender',
                            'midfielder',
                            'forward'
                        )
                    ),
                kickoff_local TEXT NOT NULL
                    CHECK (length(kickoff_local) BETWEEN 1 AND 32),
                predicted_points TEXT NOT NULL,
                appearance_probability TEXT,
                PRIMARY KEY (capture_id, source_player_id, fixture_id),
                FOREIGN KEY (capture_id, gameweek)
                    REFERENCES fpl_form_forecast_captures_v3(
                        capture_id,
                        gameweek
                    ) ON DELETE RESTRICT
            );

            INSERT INTO fpl_form_forecast_captures_v3 (
                capture_id,
                schema_version,
                source_key,
                source_url,
                season_code,
                gameweek,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                transport,
                extraction_version,
                provider_payload_sha256,
                evidence_brotli,
                player_count,
                fixture_prediction_count,
                appearance_probability_count,
                created_at_utc
            )
            SELECT
                capture_id,
                schema_version,
                source_key,
                source_url,
                season_code,
                gameweek,
                retrieved_at_utc,
                available_at_utc,
                content_sha256,
                transport,
                extraction_version,
                provider_payload_sha256,
                evidence_brotli,
                player_count,
                fixture_prediction_count,
                appearance_probability_count,
                created_at_utc
            FROM fpl_form_forecast_captures;

            INSERT INTO fpl_form_fixture_predictions_v3 (
                capture_id,
                source_player_id,
                fixture_id,
                gameweek,
                player_name,
                team_name,
                position,
                kickoff_local,
                predicted_points,
                appearance_probability
            )
            SELECT
                capture_id,
                source_player_id,
                fixture_id,
                gameweek,
                player_name,
                team_name,
                position,
                kickoff_local,
                predicted_points,
                appearance_probability
            FROM fpl_form_fixture_predictions;

            DROP TABLE fpl_form_fixture_predictions;
            DROP TABLE fpl_form_forecast_captures;

            ALTER TABLE fpl_form_forecast_captures_v3
                RENAME TO fpl_form_forecast_captures;
            ALTER TABLE fpl_form_fixture_predictions_v3
                RENAME TO fpl_form_fixture_predictions;

            CREATE INDEX fpl_form_forecast_captures_latest_idx
                ON fpl_form_forecast_captures (
                    season_code,
                    gameweek,
                    available_at_utc DESC,
                    capture_id DESC
                );
            """),
        new(
            10,
            "official-fpl-underlying-outcome-stats",
            """
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN own_goals INTEGER
                    CHECK (own_goals IS NULL OR own_goals BETWEEN 0 AND 20);
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN penalties_saved INTEGER
                    CHECK (
                        penalties_saved IS NULL
                        OR penalties_saved BETWEEN 0 AND 20
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN penalties_missed INTEGER
                    CHECK (
                        penalties_missed IS NULL
                        OR penalties_missed BETWEEN 0 AND 20
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN bps INTEGER
                    CHECK (bps IS NULL OR bps BETWEEN -500 AND 2000);
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN influence REAL
                    CHECK (
                        influence IS NULL
                        OR influence BETWEEN 0 AND 10000
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN creativity REAL
                    CHECK (
                        creativity IS NULL
                        OR creativity BETWEEN 0 AND 10000
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN threat REAL
                    CHECK (threat IS NULL OR threat BETWEEN 0 AND 10000);
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN ict_index REAL
                    CHECK (
                        ict_index IS NULL
                        OR ict_index BETWEEN 0 AND 10000
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN clearances_blocks_interceptions INTEGER
                    CHECK (
                        clearances_blocks_interceptions IS NULL
                        OR clearances_blocks_interceptions BETWEEN 0 AND 1000
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN recoveries INTEGER
                    CHECK (
                        recoveries IS NULL
                        OR recoveries BETWEEN 0 AND 1000
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN tackles INTEGER
                    CHECK (tackles IS NULL OR tackles BETWEEN 0 AND 1000);
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN defensive_contribution INTEGER
                    CHECK (
                        defensive_contribution IS NULL
                        OR defensive_contribution BETWEEN 0 AND 1000
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN expected_goals REAL
                    CHECK (
                        expected_goals IS NULL
                        OR expected_goals BETWEEN 0 AND 100
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN expected_assists REAL
                    CHECK (
                        expected_assists IS NULL
                        OR expected_assists BETWEEN 0 AND 100
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN expected_goal_involvements REAL
                    CHECK (
                        expected_goal_involvements IS NULL
                        OR expected_goal_involvements BETWEEN 0 AND 100
                    );
            ALTER TABLE official_fpl_player_outcomes
                ADD COLUMN expected_goals_conceded REAL
                    CHECK (
                        expected_goals_conceded IS NULL
                        OR expected_goals_conceded BETWEEN 0 AND 100
                    );
            """),
        new(
            11,
            "fpl-form-forecast-checks",
            """
            CREATE TABLE fpl_form_forecast_checks (
                check_id INTEGER PRIMARY KEY,
                source_key TEXT NOT NULL
                    CHECK (source_key = 'fpl-form-public-forecast/v1'),
                checked_at_utc TEXT NOT NULL,
                status TEXT NOT NULL
                    CHECK (status IN ('captured', 'waiting', 'failed')),
                reason_code TEXT
                    CHECK (
                        reason_code IS NULL
                        OR length(reason_code) BETWEEN 1 AND 64
                    ),
                capture_id INTEGER
                    REFERENCES fpl_form_forecast_captures(capture_id)
                    ON DELETE RESTRICT,
                created_at_utc TEXT NOT NULL,
                CHECK (
                    (status = 'captured'
                        AND capture_id IS NOT NULL
                        AND reason_code IS NULL)
                    OR
                    (status IN ('waiting', 'failed')
                        AND capture_id IS NULL
                        AND reason_code IS NOT NULL)
                )
            );

            CREATE INDEX fpl_form_forecast_checks_latest_idx
                ON fpl_form_forecast_checks (
                    checked_at_utc DESC,
                    check_id DESC
                );
            """),
        new(
            12,
            "official-fpl-reference-checks",
            """
            CREATE TABLE official_fpl_checks (
                check_id INTEGER PRIMARY KEY,
                checked_at_utc TEXT NOT NULL,
                status TEXT NOT NULL
                    CHECK (status IN ('captured', 'failed')),
                reason_code TEXT
                    CHECK (
                        reason_code IS NULL
                        OR length(reason_code) BETWEEN 1 AND 64
                    ),
                capture_id INTEGER
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                created_at_utc TEXT NOT NULL,
                CHECK (
                    (status = 'captured'
                        AND capture_id IS NOT NULL
                        AND reason_code IS NULL)
                    OR
                    (status = 'failed'
                        AND capture_id IS NULL
                        AND reason_code IS NOT NULL)
                )
            );

            CREATE INDEX official_fpl_checks_latest_idx
                ON official_fpl_checks (
                    checked_at_utc DESC,
                    check_id DESC
                );
            """),
        new(
            13,
            "baseline-forecast-artifacts",
            """
            CREATE TABLE baseline_forecast_artifacts (
                artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                model_key TEXT NOT NULL
                    CHECK (model_key = 'official-market-baseline-v0'),
                capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                document_json TEXT NOT NULL
                    CHECK (length(document_json) BETWEEN 2 AND 262144),
                content_sha256 TEXT NOT NULL CHECK (length(content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                UNIQUE (capture_id, model_key),
                UNIQUE (content_sha256)
            );

            CREATE INDEX baseline_forecast_artifacts_latest_idx
                ON baseline_forecast_artifacts (
                    capture_id DESC,
                    artifact_id DESC
                );
            """),
        new(
            14,
            "official-fpl-published-expected-points",
            """
            ALTER TABLE official_fpl_players
                ADD COLUMN expected_points_next TEXT
                    CHECK (
                        expected_points_next IS NULL
                        OR (
                            length(expected_points_next) BETWEEN 1 AND 16
                            AND CAST(expected_points_next AS REAL)
                                BETWEEN -20.0 AND 100.0
                        )
                    );
            """),
        new(
            15,
            "selection-revision-lifecycle",
            """
            CREATE TABLE selection_revisions (
                selection_revision_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                revision INTEGER NOT NULL CHECK (revision > 0),
                supersedes_selection_revision_id INTEGER UNIQUE
                    REFERENCES selection_revisions(selection_revision_id)
                    ON DELETE RESTRICT,
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                deadline_utc TEXT NOT NULL,
                forecast_artifact_id INTEGER NOT NULL
                    REFERENCES baseline_forecast_artifacts(artifact_id)
                    ON DELETE RESTRICT,
                forecast_artifact_content_sha256 TEXT NOT NULL
                    CHECK (length(forecast_artifact_content_sha256) = 64),
                selection_json TEXT NOT NULL
                    CHECK (length(selection_json) BETWEEN 2 AND 16384),
                selection_content_sha256 TEXT NOT NULL
                    CHECK (length(selection_content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                locked_at_utc TEXT,
                UNIQUE (season_code, gameweek, revision),
                CHECK (
                    (revision = 1 AND supersedes_selection_revision_id IS NULL)
                    OR (revision > 1 AND supersedes_selection_revision_id IS NOT NULL)
                )
            );

            CREATE INDEX selection_revisions_latest_idx
                ON selection_revisions (
                    season_code,
                    gameweek,
                    revision DESC
                );

            CREATE TRIGGER selection_revisions_immutable
            BEFORE UPDATE OF
                schema_version,
                revision,
                supersedes_selection_revision_id,
                season_code,
                gameweek,
                deadline_utc,
                forecast_artifact_id,
                forecast_artifact_content_sha256,
                selection_json,
                selection_content_sha256,
                created_at_utc
            ON selection_revisions
            BEGIN
                SELECT RAISE(ABORT, 'selection revisions are immutable');
            END;

            CREATE TRIGGER selection_revisions_lock_once
            BEFORE UPDATE OF locked_at_utc
            ON selection_revisions
            WHEN OLD.locked_at_utc IS NOT NULL OR NEW.locked_at_utc IS NULL
            BEGIN
                SELECT RAISE(ABORT, 'selection revision lock is append-only');
            END;

            CREATE TRIGGER selection_revisions_no_delete
            BEFORE DELETE ON selection_revisions
            BEGIN
                SELECT RAISE(ABORT, 'selection revisions cannot be deleted');
            END;
            """),
        new(
            16,
            "quarantined-evidence-claims",
            """
            CREATE TABLE evidence_claims (
                claim_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                status TEXT NOT NULL CHECK (status = 'quarantined'),
                source_key TEXT NOT NULL CHECK (length(source_key) BETWEEN 1 AND 100),
                canonical_url TEXT NOT NULL
                    CHECK (length(canonical_url) BETWEEN 8 AND 2048),
                author TEXT CHECK (author IS NULL OR length(author) BETWEEN 1 AND 150),
                published_at_utc TEXT,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                content_sha256 TEXT NOT NULL CHECK (length(content_sha256) = 64),
                source_revision INTEGER NOT NULL CHECK (source_revision > 0),
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                deadline_utc TEXT NOT NULL,
                player_id INTEGER NOT NULL CHECK (player_id > 0),
                identity_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                claim_type TEXT NOT NULL
                    CHECK (claim_type IN ('availability', 'start', 'minutes', 'role')),
                availability_status TEXT
                    CHECK (
                        availability_status IS NULL
                        OR availability_status IN (
                            'available',
                            'doubtful',
                            'unavailable',
                            'expected-return'
                        )
                    ),
                start_status TEXT
                    CHECK (
                        start_status IS NULL
                        OR start_status IN (
                            'starts',
                            'does-not-start',
                            'uncertain'
                        )
                    ),
                forecast_probability TEXT
                    CHECK (
                        forecast_probability IS NULL
                        OR CAST(forecast_probability AS REAL) BETWEEN 0.0 AND 1.0
                    ),
                expected_minutes INTEGER
                    CHECK (
                        expected_minutes IS NULL
                        OR expected_minutes BETWEEN 0 AND 180
                    ),
                role TEXT CHECK (role IS NULL OR length(role) BETWEEN 1 AND 100),
                directness TEXT NOT NULL
                    CHECK (
                        directness IN (
                            'direct-quote',
                            'reported',
                            'opinion',
                            'model-forecast'
                        )
                    ),
                source_span TEXT NOT NULL CHECK (length(source_span) BETWEEN 1 AND 500),
                extraction_method TEXT NOT NULL
                    CHECK (
                        extraction_method IN ('deterministic', 'human', 'llm')
                    ),
                extraction_version TEXT NOT NULL
                    CHECK (length(extraction_version) BETWEEN 1 AND 100),
                extraction_confidence TEXT NOT NULL
                    CHECK (
                        CAST(extraction_confidence AS REAL) BETWEEN 0.0 AND 1.0
                    ),
                duplicate_cluster_key TEXT
                    CHECK (
                        duplicate_cluster_key IS NULL
                        OR length(duplicate_cluster_key) = 64
                    ),
                claim_content_sha256 TEXT NOT NULL
                    UNIQUE CHECK (length(claim_content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                CHECK (
                    published_at_utc IS NULL
                    OR published_at_utc <= retrieved_at_utc
                ),
                CHECK (retrieved_at_utc <= available_at_utc),
                CHECK (
                    (claim_type = 'availability'
                        AND availability_status IS NOT NULL
                        AND start_status IS NULL
                        AND expected_minutes IS NULL
                        AND role IS NULL)
                    OR
                    (claim_type = 'start'
                        AND availability_status IS NULL
                        AND start_status IS NOT NULL
                        AND expected_minutes IS NULL
                        AND role IS NULL)
                    OR
                    (claim_type = 'minutes'
                        AND availability_status IS NULL
                        AND start_status IS NULL
                        AND forecast_probability IS NULL
                        AND expected_minutes IS NOT NULL
                        AND role IS NULL)
                    OR
                    (claim_type = 'role'
                        AND availability_status IS NULL
                        AND start_status IS NULL
                        AND forecast_probability IS NULL
                        AND expected_minutes IS NULL
                        AND role IS NOT NULL)
                )
            );

            CREATE INDEX evidence_claims_cutoff_idx
                ON evidence_claims (
                    season_code,
                    gameweek,
                    available_at_utc,
                    player_id,
                    claim_type
                );

            CREATE INDEX evidence_claims_source_score_idx
                ON evidence_claims (
                    source_key,
                    claim_type,
                    available_at_utc
                );

            CREATE TRIGGER evidence_claims_immutable
            BEFORE UPDATE ON evidence_claims
            BEGIN
                SELECT RAISE(ABORT, 'evidence claims are immutable');
            END;

            CREATE TRIGGER evidence_claims_no_delete
            BEFORE DELETE ON evidence_claims
            BEGIN
                SELECT RAISE(ABORT, 'evidence claims cannot be deleted');
            END;
            """),
        new(
            17,
            "player-gameweek-forecast-artifacts",
            """
            CREATE TABLE player_gameweek_forecast_artifacts (
                forecast_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                model_key TEXT NOT NULL
                    CHECK (
                        model_key = 'official-market-baseline-v0-player-table'
                    ),
                official_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                decision_cutoff_utc TEXT NOT NULL,
                document_json TEXT NOT NULL
                    CHECK (length(document_json) BETWEEN 2 AND 2097152),
                content_sha256 TEXT NOT NULL UNIQUE
                    CHECK (length(content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                UNIQUE (official_capture_id, model_key)
            );

            CREATE INDEX player_gameweek_forecast_artifacts_latest_idx
                ON player_gameweek_forecast_artifacts (
                    season_code,
                    gameweek,
                    decision_cutoff_utc DESC,
                    forecast_artifact_id DESC
                );

            CREATE TRIGGER player_gameweek_forecast_artifacts_immutable
            BEFORE UPDATE ON player_gameweek_forecast_artifacts
            BEGIN
                SELECT RAISE(ABORT, 'player forecast artifacts are immutable');
            END;

            CREATE TRIGGER player_gameweek_forecast_artifacts_no_delete
            BEFORE DELETE ON player_gameweek_forecast_artifacts
            BEGIN
                SELECT RAISE(ABORT, 'player forecast artifacts cannot be deleted');
            END;
            """),
        new(
            18,
            "research-source-snapshots",
            """
            CREATE TABLE research_source_snapshots (
                snapshot_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                status TEXT NOT NULL CHECK (status = 'shadow-only'),
                source_key TEXT NOT NULL
                    CHECK (
                        source_key IN (
                            'premier-league-injuries',
                            'ffscout-predicted-lineups',
                            'straightred-lineup-consensus'
                        )
                    ),
                source_class TEXT NOT NULL CHECK (length(source_class) BETWEEN 4 AND 100),
                canonical_url TEXT NOT NULL CHECK (length(canonical_url) BETWEEN 8 AND 2048),
                final_url TEXT NOT NULL CHECK (length(final_url) BETWEEN 8 AND 2048),
                dependence_group TEXT NOT NULL
                    CHECK (length(dependence_group) BETWEEN 4 AND 100),
                transport_key TEXT NOT NULL CHECK (transport_key = 'spider-mcp'),
                transport_version TEXT NOT NULL
                    CHECK (length(transport_version) BETWEEN 4 AND 100),
                season_code TEXT NOT NULL CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                deadline_utc TEXT NOT NULL,
                identity_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                source_revision INTEGER NOT NULL CHECK (source_revision > 0),
                content_sha256 TEXT NOT NULL CHECK (length(content_sha256) = 64),
                content_bytes INTEGER NOT NULL
                    CHECK (content_bytes BETWEEN 1 AND 131072),
                content_brotli BLOB NOT NULL
                    CHECK (length(content_brotli) BETWEEN 1 AND 196608),
                created_at_utc TEXT NOT NULL,
                UNIQUE (source_key, source_revision),
                UNIQUE (source_key, identity_capture_id, content_sha256)
            );

            CREATE INDEX research_source_snapshots_target_idx
                ON research_source_snapshots (
                    season_code,
                    gameweek,
                    available_at_utc,
                    source_key
                );

            CREATE TRIGGER research_source_snapshots_immutable
            BEFORE UPDATE ON research_source_snapshots
            BEGIN
                SELECT RAISE(ABORT, 'research source snapshots are immutable');
            END;

            CREATE TRIGGER research_source_snapshots_no_delete
            BEFORE DELETE ON research_source_snapshots
            BEGIN
                SELECT RAISE(ABORT, 'research source snapshots cannot be deleted');
            END;
            """),
        new(
            19,
            "historical-fpl-season-archive",
            """
            CREATE TABLE historical_fpl_season_captures (
                capture_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                source_key TEXT NOT NULL
                    CHECK (source_key = 'vaastav-fpl-historical/v1'),
                season_code TEXT NOT NULL CHECK (season_code = '2025-26'),
                source_revision TEXT NOT NULL CHECK (length(source_revision) = 40),
                players_url TEXT NOT NULL CHECK (length(players_url) BETWEEN 8 AND 2048),
                gameweeks_url TEXT NOT NULL
                    CHECK (length(gameweeks_url) BETWEEN 8 AND 2048),
                published_at_utc TEXT NOT NULL,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                players_sha256 TEXT NOT NULL CHECK (length(players_sha256) = 64),
                gameweeks_sha256 TEXT NOT NULL CHECK (length(gameweeks_sha256) = 64),
                players_csv_brotli BLOB NOT NULL
                    CHECK (length(players_csv_brotli) BETWEEN 1 AND 8388608),
                gameweeks_csv_brotli BLOB NOT NULL
                    CHECK (length(gameweeks_csv_brotli) BETWEEN 1 AND 8388608),
                player_count INTEGER NOT NULL CHECK (player_count BETWEEN 1 AND 2000),
                player_gameweek_count INTEGER NOT NULL
                    CHECK (player_gameweek_count BETWEEN 1 AND 100000),
                stable_code_count INTEGER NOT NULL
                    CHECK (stable_code_count BETWEEN 1 AND 2000),
                created_at_utc TEXT NOT NULL,
                UNIQUE (source_key, season_code, source_revision),
                UNIQUE (players_sha256, gameweeks_sha256),
                CHECK (published_at_utc <= retrieved_at_utc),
                CHECK (retrieved_at_utc <= available_at_utc),
                CHECK (stable_code_count <= player_count)
            );

            CREATE TABLE historical_fpl_players (
                capture_id INTEGER NOT NULL
                    REFERENCES historical_fpl_season_captures(capture_id)
                    ON DELETE RESTRICT,
                season_element_id INTEGER NOT NULL CHECK (season_element_id > 0),
                player_code INTEGER NOT NULL CHECK (player_code > 0),
                first_name TEXT NOT NULL CHECK (length(first_name) BETWEEN 1 AND 100),
                second_name TEXT NOT NULL CHECK (length(second_name) BETWEEN 1 AND 100),
                web_name TEXT NOT NULL CHECK (length(web_name) BETWEEN 1 AND 100),
                position TEXT NOT NULL
                    CHECK (
                        position IN (
                            'goalkeeper',
                            'defender',
                            'midfielder',
                            'forward'
                        )
                    ),
                final_team_id INTEGER NOT NULL CHECK (final_team_id > 0),
                final_status TEXT NOT NULL CHECK (length(final_status) BETWEEN 1 AND 8),
                final_chance_next_round INTEGER
                    CHECK (final_chance_next_round BETWEEN 0 AND 100),
                final_news_sha256 TEXT NOT NULL CHECK (length(final_news_sha256) = 64),
                final_news_added_utc TEXT,
                PRIMARY KEY (capture_id, season_element_id),
                UNIQUE (capture_id, player_code)
            );

            CREATE INDEX historical_fpl_players_code_idx
                ON historical_fpl_players (player_code, capture_id);

            CREATE TABLE historical_fpl_player_gameweeks (
                capture_id INTEGER NOT NULL,
                season_element_id INTEGER NOT NULL,
                player_code INTEGER NOT NULL,
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                fixture_id INTEGER NOT NULL CHECK (fixture_id > 0),
                kickoff_utc TEXT NOT NULL,
                team_name TEXT NOT NULL CHECK (length(team_name) BETWEEN 1 AND 100),
                opponent_team_id INTEGER NOT NULL CHECK (opponent_team_id > 0),
                was_home INTEGER NOT NULL CHECK (was_home IN (0, 1)),
                minutes INTEGER NOT NULL CHECK (minutes BETWEEN 0 AND 180),
                starts INTEGER NOT NULL CHECK (starts BETWEEN 0 AND 2),
                total_points INTEGER NOT NULL CHECK (total_points BETWEEN -50 AND 100),
                goals_scored INTEGER NOT NULL CHECK (goals_scored BETWEEN 0 AND 10),
                assists INTEGER NOT NULL CHECK (assists BETWEEN 0 AND 10),
                clean_sheets INTEGER NOT NULL CHECK (clean_sheets BETWEEN 0 AND 2),
                goals_conceded INTEGER NOT NULL CHECK (goals_conceded BETWEEN 0 AND 20),
                saves INTEGER NOT NULL CHECK (saves BETWEEN 0 AND 30),
                bonus INTEGER NOT NULL CHECK (bonus BETWEEN 0 AND 6),
                yellow_cards INTEGER NOT NULL CHECK (yellow_cards BETWEEN 0 AND 2),
                red_cards INTEGER NOT NULL CHECK (red_cards BETWEEN 0 AND 2),
                bps INTEGER NOT NULL CHECK (bps BETWEEN -100 AND 300),
                influence TEXT NOT NULL,
                creativity TEXT NOT NULL,
                threat TEXT NOT NULL,
                ict_index TEXT NOT NULL,
                expected_goals TEXT NOT NULL,
                expected_assists TEXT NOT NULL,
                expected_goal_involvements TEXT NOT NULL,
                expected_goals_conceded TEXT NOT NULL,
                clearances_blocks_interceptions INTEGER NOT NULL
                    CHECK (clearances_blocks_interceptions BETWEEN 0 AND 100),
                defensive_contribution INTEGER NOT NULL
                    CHECK (defensive_contribution BETWEEN 0 AND 100),
                recoveries INTEGER NOT NULL CHECK (recoveries BETWEEN 0 AND 100),
                tackles INTEGER NOT NULL CHECK (tackles BETWEEN 0 AND 100),
                PRIMARY KEY (capture_id, season_element_id, gameweek, fixture_id),
                FOREIGN KEY (capture_id, season_element_id)
                    REFERENCES historical_fpl_players(capture_id, season_element_id)
                    ON DELETE RESTRICT,
                FOREIGN KEY (capture_id, player_code)
                    REFERENCES historical_fpl_players(capture_id, player_code)
                    ON DELETE RESTRICT
            );

            CREATE INDEX historical_fpl_player_gameweeks_code_idx
                ON historical_fpl_player_gameweeks (
                    player_code,
                    gameweek,
                    kickoff_utc
                );

            CREATE TRIGGER historical_fpl_season_captures_immutable
            BEFORE UPDATE ON historical_fpl_season_captures
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL season captures are immutable');
            END;

            CREATE TRIGGER historical_fpl_season_captures_no_delete
            BEFORE DELETE ON historical_fpl_season_captures
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL season captures cannot be deleted');
            END;

            CREATE TRIGGER historical_fpl_players_immutable
            BEFORE UPDATE ON historical_fpl_players
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL players are immutable');
            END;

            CREATE TRIGGER historical_fpl_players_no_delete
            BEFORE DELETE ON historical_fpl_players
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL players cannot be deleted');
            END;

            CREATE TRIGGER historical_fpl_player_gameweeks_immutable
            BEFORE UPDATE ON historical_fpl_player_gameweeks
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL player Gameweeks are immutable');
            END;

            CREATE TRIGGER historical_fpl_player_gameweeks_no_delete
            BEFORE DELETE ON historical_fpl_player_gameweeks
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL player Gameweeks cannot be deleted');
            END;
            """),
        new(
            20,
            "preseason-player-forecast-artifact",
            """
            CREATE TABLE preseason_player_forecast_artifacts (
                forecast_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                artifact_type TEXT NOT NULL
                    CHECK (
                        artifact_type =
                            'historical-preseason-player-gameweek-forecast'
                    ),
                status TEXT NOT NULL
                    CHECK (status = 'provisional-preseason-challenger'),
                model_key TEXT NOT NULL
                    CHECK (model_key = 'historical-preseason-histogram-tree-v1'),
                official_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                historical_capture_id INTEGER NOT NULL
                    REFERENCES historical_fpl_season_captures(capture_id)
                    ON DELETE RESTRICT,
                season_code TEXT NOT NULL CHECK (season_code = '2026-27'),
                gameweek INTEGER NOT NULL CHECK (gameweek = 1),
                decision_cutoff_utc TEXT NOT NULL,
                producer_run_identity_sha256 TEXT NOT NULL
                    CHECK (length(producer_run_identity_sha256) = 64),
                document_json TEXT NOT NULL
                    CHECK (length(document_json) BETWEEN 2 AND 2097152),
                content_sha256 TEXT NOT NULL UNIQUE
                    CHECK (length(content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                UNIQUE (official_capture_id, model_key)
            );

            CREATE INDEX preseason_player_forecast_artifacts_latest_idx
                ON preseason_player_forecast_artifacts (
                    season_code,
                    gameweek,
                    decision_cutoff_utc DESC,
                    forecast_artifact_id DESC
                );

            CREATE TRIGGER preseason_player_forecast_artifacts_immutable
            BEFORE UPDATE ON preseason_player_forecast_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'preseason player forecast artifacts are immutable'
                );
            END;

            CREATE TRIGGER preseason_player_forecast_artifacts_no_delete
            BEFORE DELETE ON preseason_player_forecast_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'preseason player forecast artifacts cannot be deleted'
                );
            END;
            """),
        new(
            21,
            "byparr-research-source-captures",
            """
            CREATE TABLE research_source_snapshots_v21 (
                snapshot_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                status TEXT NOT NULL CHECK (status = 'shadow-only'),
                source_key TEXT NOT NULL
                    CHECK (
                        source_key IN (
                            'premier-league-injuries',
                            'ffscout-predicted-lineups',
                            'straightred-lineup-consensus',
                            'fbref-championship-playing-time-2025-26'
                        )
                    ),
                source_class TEXT NOT NULL
                    CHECK (length(source_class) BETWEEN 4 AND 100),
                canonical_url TEXT NOT NULL
                    CHECK (length(canonical_url) BETWEEN 8 AND 2048),
                final_url TEXT NOT NULL
                    CHECK (length(final_url) BETWEEN 8 AND 2048),
                dependence_group TEXT NOT NULL
                    CHECK (length(dependence_group) BETWEEN 4 AND 100),
                transport_key TEXT NOT NULL
                    CHECK (transport_key IN ('spider-mcp', 'byparr')),
                transport_version TEXT NOT NULL
                    CHECK (length(transport_version) BETWEEN 4 AND 100),
                season_code TEXT NOT NULL
                    CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                deadline_utc TEXT NOT NULL,
                identity_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                source_revision INTEGER NOT NULL CHECK (source_revision > 0),
                content_sha256 TEXT NOT NULL
                    CHECK (length(content_sha256) = 64),
                content_bytes INTEGER NOT NULL
                    CHECK (content_bytes BETWEEN 1 AND 6291456),
                content_brotli BLOB NOT NULL
                    CHECK (length(content_brotli) BETWEEN 1 AND 8388608),
                created_at_utc TEXT NOT NULL,
                UNIQUE (source_key, source_revision),
                UNIQUE (source_key, identity_capture_id, content_sha256)
            );

            INSERT INTO research_source_snapshots_v21 (
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                content_brotli,
                created_at_utc
            )
            SELECT
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                content_brotli,
                created_at_utc
            FROM research_source_snapshots;

            DROP TABLE research_source_snapshots;
            ALTER TABLE research_source_snapshots_v21
                RENAME TO research_source_snapshots;

            CREATE INDEX research_source_snapshots_target_idx
                ON research_source_snapshots (
                    season_code,
                    gameweek,
                    available_at_utc,
                    source_key
                );

            CREATE TRIGGER research_source_snapshots_immutable
            BEFORE UPDATE ON research_source_snapshots
            BEGIN
                SELECT RAISE(ABORT, 'research source snapshots are immutable');
            END;

            CREATE TRIGGER research_source_snapshots_no_delete
            BEFORE DELETE ON research_source_snapshots
            BEGIN
                SELECT RAISE(ABORT, 'research source snapshots cannot be deleted');
            END;
            """),
        new(
            22,
            "reviewed-fbref-player-match-log-captures",
            """
            CREATE TABLE research_source_snapshots_v22 (
                snapshot_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                status TEXT NOT NULL CHECK (status = 'shadow-only'),
                source_key TEXT NOT NULL
                    CHECK (
                        source_key IN (
                            'premier-league-injuries',
                            'ffscout-predicted-lineups',
                            'straightred-lineup-consensus',
                            'fbref-championship-playing-time-2025-26'
                        )
                        OR (
                            length(source_key) = 39
                            AND source_key GLOB
                                'fbref-player-match-log-[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]-2025-26'
                        )
                    ),
                source_class TEXT NOT NULL
                    CHECK (length(source_class) BETWEEN 4 AND 100),
                canonical_url TEXT NOT NULL
                    CHECK (length(canonical_url) BETWEEN 8 AND 2048),
                final_url TEXT NOT NULL
                    CHECK (length(final_url) BETWEEN 8 AND 2048),
                dependence_group TEXT NOT NULL
                    CHECK (length(dependence_group) BETWEEN 4 AND 100),
                transport_key TEXT NOT NULL
                    CHECK (transport_key IN ('spider-mcp', 'byparr')),
                transport_version TEXT NOT NULL
                    CHECK (length(transport_version) BETWEEN 4 AND 100),
                season_code TEXT NOT NULL
                    CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                deadline_utc TEXT NOT NULL,
                identity_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                source_revision INTEGER NOT NULL CHECK (source_revision > 0),
                content_sha256 TEXT NOT NULL
                    CHECK (length(content_sha256) = 64),
                content_bytes INTEGER NOT NULL
                    CHECK (content_bytes BETWEEN 1 AND 6291456),
                content_brotli BLOB NOT NULL
                    CHECK (length(content_brotli) BETWEEN 1 AND 8388608),
                created_at_utc TEXT NOT NULL,
                UNIQUE (source_key, source_revision),
                UNIQUE (source_key, identity_capture_id, content_sha256)
            );

            INSERT INTO research_source_snapshots_v22 (
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                content_brotli,
                created_at_utc
            )
            SELECT
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                content_brotli,
                created_at_utc
            FROM research_source_snapshots;

            DROP TABLE research_source_snapshots;
            ALTER TABLE research_source_snapshots_v22
                RENAME TO research_source_snapshots;

            CREATE INDEX research_source_snapshots_target_idx
                ON research_source_snapshots (
                    season_code,
                    gameweek,
                    available_at_utc,
                    source_key
                );

            CREATE TRIGGER research_source_snapshots_immutable
            BEFORE UPDATE ON research_source_snapshots
            BEGIN
                SELECT RAISE(ABORT, 'research source snapshots are immutable');
            END;

            CREATE TRIGGER research_source_snapshots_no_delete
            BEFORE DELETE ON research_source_snapshots
            BEGIN
                SELECT RAISE(ABORT, 'research source snapshots cannot be deleted');
            END;
            """),
        new(
            23,
            "fbref-promoted-club-team-schedules",
            """
            CREATE TABLE research_source_snapshots_v23 (
                snapshot_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                status TEXT NOT NULL CHECK (status = 'shadow-only'),
                source_key TEXT NOT NULL
                    CHECK (
                        source_key IN (
                            'premier-league-injuries',
                            'ffscout-predicted-lineups',
                            'straightred-lineup-consensus',
                            'fbref-championship-playing-time-2025-26',
                            'fbref-team-schedule-f7e3dfe9-2025-26',
                            'fbref-team-schedule-bd8769d1-2025-26',
                            'fbref-team-schedule-b74092de-2025-26'
                        )
                        OR (
                            length(source_key) = 39
                            AND source_key GLOB
                                'fbref-player-match-log-[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]-2025-26'
                        )
                    ),
                source_class TEXT NOT NULL
                    CHECK (length(source_class) BETWEEN 4 AND 100),
                canonical_url TEXT NOT NULL
                    CHECK (length(canonical_url) BETWEEN 8 AND 2048),
                final_url TEXT NOT NULL
                    CHECK (length(final_url) BETWEEN 8 AND 2048),
                dependence_group TEXT NOT NULL
                    CHECK (length(dependence_group) BETWEEN 4 AND 100),
                transport_key TEXT NOT NULL
                    CHECK (transport_key IN ('spider-mcp', 'byparr')),
                transport_version TEXT NOT NULL
                    CHECK (length(transport_version) BETWEEN 4 AND 100),
                season_code TEXT NOT NULL
                    CHECK (length(season_code) BETWEEN 4 AND 16),
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                deadline_utc TEXT NOT NULL,
                identity_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                source_revision INTEGER NOT NULL CHECK (source_revision > 0),
                content_sha256 TEXT NOT NULL
                    CHECK (length(content_sha256) = 64),
                content_bytes INTEGER NOT NULL
                    CHECK (content_bytes BETWEEN 1 AND 6291456),
                content_brotli BLOB NOT NULL
                    CHECK (length(content_brotli) BETWEEN 1 AND 8388608),
                created_at_utc TEXT NOT NULL,
                UNIQUE (source_key, source_revision),
                UNIQUE (source_key, identity_capture_id, content_sha256)
            );

            INSERT INTO research_source_snapshots_v23 (
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                content_brotli,
                created_at_utc
            )
            SELECT
                snapshot_id,
                schema_version,
                status,
                source_key,
                source_class,
                canonical_url,
                final_url,
                dependence_group,
                transport_key,
                transport_version,
                season_code,
                gameweek,
                deadline_utc,
                identity_capture_id,
                retrieved_at_utc,
                available_at_utc,
                source_revision,
                content_sha256,
                content_bytes,
                content_brotli,
                created_at_utc
            FROM research_source_snapshots;

            DROP TABLE research_source_snapshots;
            ALTER TABLE research_source_snapshots_v23
                RENAME TO research_source_snapshots;

            CREATE INDEX research_source_snapshots_target_idx
                ON research_source_snapshots (
                    season_code,
                    gameweek,
                    available_at_utc,
                    source_key
                );

            CREATE TRIGGER research_source_snapshots_immutable
            BEFORE UPDATE ON research_source_snapshots
            BEGIN
                SELECT RAISE(ABORT, 'research source snapshots are immutable');
            END;

            CREATE TRIGGER research_source_snapshots_no_delete
            BEFORE DELETE ON research_source_snapshots
            BEGIN
                SELECT RAISE(ABORT, 'research source snapshots cannot be deleted');
            END;
            """),
        new(
            24,
            "two-season-historical-fpl-archive",
            """
            PRAGMA defer_foreign_keys = ON;

            CREATE TABLE historical_fpl_season_captures_v24 (
                capture_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                source_key TEXT NOT NULL
                    CHECK (source_key = 'vaastav-fpl-historical/v1'),
                season_code TEXT NOT NULL
                    CHECK (season_code IN ('2024-25', '2025-26')),
                source_revision TEXT NOT NULL CHECK (length(source_revision) = 40),
                players_url TEXT NOT NULL CHECK (length(players_url) BETWEEN 8 AND 2048),
                gameweeks_url TEXT NOT NULL
                    CHECK (length(gameweeks_url) BETWEEN 8 AND 2048),
                published_at_utc TEXT NOT NULL,
                retrieved_at_utc TEXT NOT NULL,
                available_at_utc TEXT NOT NULL,
                players_sha256 TEXT NOT NULL CHECK (length(players_sha256) = 64),
                gameweeks_sha256 TEXT NOT NULL CHECK (length(gameweeks_sha256) = 64),
                players_csv_brotli BLOB NOT NULL
                    CHECK (length(players_csv_brotli) BETWEEN 1 AND 8388608),
                gameweeks_csv_brotli BLOB NOT NULL
                    CHECK (length(gameweeks_csv_brotli) BETWEEN 1 AND 8388608),
                player_count INTEGER NOT NULL CHECK (player_count BETWEEN 1 AND 2000),
                player_gameweek_count INTEGER NOT NULL
                    CHECK (player_gameweek_count BETWEEN 1 AND 100000),
                stable_code_count INTEGER NOT NULL
                    CHECK (stable_code_count BETWEEN 1 AND 2000),
                created_at_utc TEXT NOT NULL,
                UNIQUE (source_key, season_code, source_revision),
                UNIQUE (players_sha256, gameweeks_sha256),
                CHECK (published_at_utc <= retrieved_at_utc),
                CHECK (retrieved_at_utc <= available_at_utc),
                CHECK (stable_code_count <= player_count)
            );

            INSERT INTO historical_fpl_season_captures_v24
            SELECT * FROM historical_fpl_season_captures;

            CREATE TABLE historical_fpl_players_v24 (
                capture_id INTEGER NOT NULL
                    REFERENCES historical_fpl_season_captures_v24(capture_id)
                    ON DELETE RESTRICT,
                season_element_id INTEGER NOT NULL CHECK (season_element_id > 0),
                player_code INTEGER NOT NULL CHECK (player_code > 0),
                first_name TEXT NOT NULL CHECK (length(first_name) BETWEEN 1 AND 100),
                second_name TEXT NOT NULL CHECK (length(second_name) BETWEEN 1 AND 100),
                web_name TEXT NOT NULL CHECK (length(web_name) BETWEEN 1 AND 100),
                position TEXT NOT NULL
                    CHECK (
                        position IN (
                            'goalkeeper',
                            'defender',
                            'midfielder',
                            'forward'
                        )
                    ),
                final_team_id INTEGER NOT NULL CHECK (final_team_id > 0),
                final_status TEXT NOT NULL CHECK (length(final_status) BETWEEN 1 AND 8),
                final_chance_next_round INTEGER
                    CHECK (final_chance_next_round BETWEEN 0 AND 100),
                final_news_sha256 TEXT NOT NULL CHECK (length(final_news_sha256) = 64),
                final_news_added_utc TEXT,
                PRIMARY KEY (capture_id, season_element_id),
                UNIQUE (capture_id, player_code)
            );

            INSERT INTO historical_fpl_players_v24
            SELECT * FROM historical_fpl_players;

            CREATE TABLE historical_fpl_player_gameweeks_v24 (
                capture_id INTEGER NOT NULL,
                season_element_id INTEGER NOT NULL,
                player_code INTEGER NOT NULL,
                gameweek INTEGER NOT NULL CHECK (gameweek BETWEEN 1 AND 38),
                fixture_id INTEGER NOT NULL CHECK (fixture_id > 0),
                kickoff_utc TEXT NOT NULL,
                team_name TEXT NOT NULL CHECK (length(team_name) BETWEEN 1 AND 100),
                opponent_team_id INTEGER NOT NULL CHECK (opponent_team_id > 0),
                was_home INTEGER NOT NULL CHECK (was_home IN (0, 1)),
                minutes INTEGER NOT NULL CHECK (minutes BETWEEN 0 AND 180),
                starts INTEGER NOT NULL CHECK (starts BETWEEN 0 AND 2),
                total_points INTEGER NOT NULL CHECK (total_points BETWEEN -50 AND 100),
                goals_scored INTEGER NOT NULL CHECK (goals_scored BETWEEN 0 AND 10),
                assists INTEGER NOT NULL CHECK (assists BETWEEN 0 AND 10),
                clean_sheets INTEGER NOT NULL CHECK (clean_sheets BETWEEN 0 AND 2),
                goals_conceded INTEGER NOT NULL CHECK (goals_conceded BETWEEN 0 AND 20),
                saves INTEGER NOT NULL CHECK (saves BETWEEN 0 AND 30),
                bonus INTEGER NOT NULL CHECK (bonus BETWEEN 0 AND 6),
                yellow_cards INTEGER NOT NULL CHECK (yellow_cards BETWEEN 0 AND 2),
                red_cards INTEGER NOT NULL CHECK (red_cards BETWEEN 0 AND 2),
                bps INTEGER NOT NULL CHECK (bps BETWEEN -100 AND 300),
                influence TEXT NOT NULL,
                creativity TEXT NOT NULL,
                threat TEXT NOT NULL,
                ict_index TEXT NOT NULL,
                expected_goals TEXT NOT NULL,
                expected_assists TEXT NOT NULL,
                expected_goal_involvements TEXT NOT NULL,
                expected_goals_conceded TEXT NOT NULL,
                clearances_blocks_interceptions INTEGER
                    CHECK (clearances_blocks_interceptions BETWEEN 0 AND 100),
                defensive_contribution INTEGER
                    CHECK (defensive_contribution BETWEEN 0 AND 100),
                recoveries INTEGER CHECK (recoveries BETWEEN 0 AND 100),
                tackles INTEGER CHECK (tackles BETWEEN 0 AND 100),
                PRIMARY KEY (capture_id, season_element_id, gameweek, fixture_id),
                FOREIGN KEY (capture_id, season_element_id)
                    REFERENCES historical_fpl_players_v24(
                        capture_id,
                        season_element_id
                    )
                    ON DELETE RESTRICT,
                FOREIGN KEY (capture_id, player_code)
                    REFERENCES historical_fpl_players_v24(capture_id, player_code)
                    ON DELETE RESTRICT
            );

            INSERT INTO historical_fpl_player_gameweeks_v24
            SELECT * FROM historical_fpl_player_gameweeks;

            CREATE TABLE preseason_player_forecast_artifacts_v24 (
                forecast_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                artifact_type TEXT NOT NULL
                    CHECK (
                        artifact_type =
                            'historical-preseason-player-gameweek-forecast'
                    ),
                status TEXT NOT NULL
                    CHECK (status = 'provisional-preseason-challenger'),
                model_key TEXT NOT NULL
                    CHECK (model_key = 'historical-preseason-histogram-tree-v1'),
                official_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                historical_capture_id INTEGER NOT NULL
                    REFERENCES historical_fpl_season_captures_v24(capture_id)
                    ON DELETE RESTRICT,
                season_code TEXT NOT NULL CHECK (season_code = '2026-27'),
                gameweek INTEGER NOT NULL CHECK (gameweek = 1),
                decision_cutoff_utc TEXT NOT NULL,
                producer_run_identity_sha256 TEXT NOT NULL
                    CHECK (length(producer_run_identity_sha256) = 64),
                document_json TEXT NOT NULL
                    CHECK (length(document_json) BETWEEN 2 AND 2097152),
                content_sha256 TEXT NOT NULL UNIQUE
                    CHECK (length(content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                UNIQUE (official_capture_id, model_key)
            );

            INSERT INTO preseason_player_forecast_artifacts_v24
            SELECT * FROM preseason_player_forecast_artifacts;

            DROP TRIGGER preseason_player_forecast_artifacts_immutable;
            DROP TRIGGER preseason_player_forecast_artifacts_no_delete;
            DROP TRIGGER historical_fpl_player_gameweeks_immutable;
            DROP TRIGGER historical_fpl_player_gameweeks_no_delete;
            DROP TRIGGER historical_fpl_players_immutable;
            DROP TRIGGER historical_fpl_players_no_delete;
            DROP TRIGGER historical_fpl_season_captures_immutable;
            DROP TRIGGER historical_fpl_season_captures_no_delete;

            DROP TABLE preseason_player_forecast_artifacts;
            DROP TABLE historical_fpl_player_gameweeks;
            DROP TABLE historical_fpl_players;
            DROP TABLE historical_fpl_season_captures;

            ALTER TABLE historical_fpl_season_captures_v24
                RENAME TO historical_fpl_season_captures;
            ALTER TABLE historical_fpl_players_v24
                RENAME TO historical_fpl_players;
            ALTER TABLE historical_fpl_player_gameweeks_v24
                RENAME TO historical_fpl_player_gameweeks;
            ALTER TABLE preseason_player_forecast_artifacts_v24
                RENAME TO preseason_player_forecast_artifacts;

            CREATE INDEX historical_fpl_players_code_idx
                ON historical_fpl_players (player_code, capture_id);

            CREATE INDEX historical_fpl_player_gameweeks_code_idx
                ON historical_fpl_player_gameweeks (
                    player_code,
                    gameweek,
                    kickoff_utc
                );

            CREATE INDEX preseason_player_forecast_artifacts_latest_idx
                ON preseason_player_forecast_artifacts (
                    season_code,
                    gameweek,
                    decision_cutoff_utc DESC,
                    forecast_artifact_id DESC
                );

            CREATE TRIGGER historical_fpl_season_captures_immutable
            BEFORE UPDATE ON historical_fpl_season_captures
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL season captures are immutable');
            END;

            CREATE TRIGGER historical_fpl_season_captures_no_delete
            BEFORE DELETE ON historical_fpl_season_captures
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL season captures cannot be deleted');
            END;

            CREATE TRIGGER historical_fpl_players_immutable
            BEFORE UPDATE ON historical_fpl_players
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL players are immutable');
            END;

            CREATE TRIGGER historical_fpl_players_no_delete
            BEFORE DELETE ON historical_fpl_players
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL players cannot be deleted');
            END;

            CREATE TRIGGER historical_fpl_player_gameweeks_immutable
            BEFORE UPDATE ON historical_fpl_player_gameweeks
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL player Gameweeks are immutable');
            END;

            CREATE TRIGGER historical_fpl_player_gameweeks_no_delete
            BEFORE DELETE ON historical_fpl_player_gameweeks
            BEGIN
                SELECT RAISE(ABORT, 'historical FPL player Gameweeks cannot be deleted');
            END;

            CREATE TRIGGER preseason_player_forecast_artifacts_immutable
            BEFORE UPDATE ON preseason_player_forecast_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'preseason player forecast artifacts are immutable'
                );
            END;

            CREATE TRIGGER preseason_player_forecast_artifacts_no_delete
            BEFORE DELETE ON preseason_player_forecast_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'preseason player forecast artifacts cannot be deleted'
                );
            END;
            """),
        new(
            25,
            "multi-season-player-forecast-artifact",
            """
            CREATE TABLE multi_season_player_forecast_artifacts (
                forecast_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL CHECK (schema_version = '1.0'),
                artifact_type TEXT NOT NULL
                    CHECK (
                        artifact_type =
                            'multi-season-preseason-shadow-player-gameweek-forecast'
                    ),
                status TEXT NOT NULL
                    CHECK (
                        status =
                            'retrospective-screen-shadow-challenger'
                    ),
                model_key TEXT NOT NULL
                    CHECK (model_key = 'multi-season-histogram-tree-v1'),
                official_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                older_historical_capture_id INTEGER NOT NULL
                    REFERENCES historical_fpl_season_captures(capture_id)
                    ON DELETE RESTRICT,
                latest_historical_capture_id INTEGER NOT NULL
                    REFERENCES historical_fpl_season_captures(capture_id)
                    ON DELETE RESTRICT,
                season_code TEXT NOT NULL CHECK (season_code = '2026-27'),
                gameweek INTEGER NOT NULL CHECK (gameweek = 1),
                decision_cutoff_utc TEXT NOT NULL,
                producer_run_identity_sha256 TEXT NOT NULL
                    CHECK (length(producer_run_identity_sha256) = 64),
                document_json TEXT NOT NULL
                    CHECK (length(document_json) BETWEEN 2 AND 2097152),
                content_sha256 TEXT NOT NULL UNIQUE
                    CHECK (length(content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                CHECK (
                    older_historical_capture_id
                        <> latest_historical_capture_id
                ),
                UNIQUE (official_capture_id, model_key)
            );

            CREATE INDEX multi_season_player_forecast_artifacts_latest_idx
                ON multi_season_player_forecast_artifacts (
                    season_code,
                    gameweek,
                    decision_cutoff_utc DESC,
                    forecast_artifact_id DESC
                );

            CREATE TRIGGER multi_season_player_forecast_artifacts_immutable
            BEFORE UPDATE ON multi_season_player_forecast_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'multi-season player forecast artifacts are immutable'
                );
            END;

            CREATE TRIGGER multi_season_player_forecast_artifacts_no_delete
            BEFORE DELETE ON multi_season_player_forecast_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'multi-season player forecast artifacts cannot be deleted'
                );
            END;
            """),
        new(
            26,
            "joint-scenario-shadow-artifact",
            """
            CREATE TABLE joint_scenario_shadow_artifacts (
                scenario_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL
                    CHECK (schema_version = '1.0'),
                artifact_type TEXT NOT NULL
                    CHECK (
                        artifact_type =
                            'current-joint-player-gameweek-scenario-shadow'
                    ),
                artifact_version TEXT NOT NULL
                    CHECK (
                        artifact_version =
                            'current-joint-scenario-shadow-v1'
                    ),
                status TEXT NOT NULL
                    CHECK (status = 'prospective-shadow-unscored'),
                scenario_model_key TEXT NOT NULL
                    CHECK (
                        scenario_model_key =
                            'joint-gameweek-residual-bootstrap'
                    ),
                official_capture_id INTEGER NOT NULL
                    REFERENCES official_fpl_captures(capture_id)
                    ON DELETE RESTRICT,
                source_historical_capture_id INTEGER NOT NULL
                    REFERENCES historical_fpl_season_captures(capture_id)
                    ON DELETE RESTRICT,
                point_forecast_artifact_id INTEGER NOT NULL
                    REFERENCES multi_season_player_forecast_artifacts(
                        forecast_artifact_id
                    )
                    ON DELETE RESTRICT,
                season_code TEXT NOT NULL CHECK (season_code = '2026-27'),
                gameweek INTEGER NOT NULL CHECK (gameweek = 1),
                decision_cutoff_utc TEXT NOT NULL,
                scenario_count INTEGER NOT NULL
                    CHECK (scenario_count BETWEEN 1 AND 512),
                player_count INTEGER NOT NULL
                    CHECK (player_count BETWEEN 1 AND 1024),
                scenario_content_sha256 TEXT NOT NULL UNIQUE
                    CHECK (length(scenario_content_sha256) = 64),
                producer_run_identity_sha256 TEXT NOT NULL
                    CHECK (length(producer_run_identity_sha256) = 64),
                document_json TEXT NOT NULL
                    CHECK (length(document_json) BETWEEN 2 AND 2097152),
                content_sha256 TEXT NOT NULL UNIQUE
                    CHECK (length(content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                UNIQUE (official_capture_id, scenario_model_key)
            );

            CREATE INDEX joint_scenario_shadow_artifacts_latest_idx
                ON joint_scenario_shadow_artifacts (
                    season_code,
                    gameweek,
                    decision_cutoff_utc DESC,
                    scenario_artifact_id DESC
                );

            CREATE TRIGGER joint_scenario_shadow_artifacts_immutable
            BEFORE UPDATE ON joint_scenario_shadow_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'joint scenario shadow artifacts are immutable'
                );
            END;

            CREATE TRIGGER joint_scenario_shadow_artifacts_no_delete
            BEFORE DELETE ON joint_scenario_shadow_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'joint scenario shadow artifacts cannot be deleted'
                );
            END;
            """),
        new(
            27,
            "selection-scenario-score-shadow-artifact",
            """
            CREATE TABLE selection_scenario_score_shadow_artifacts (
                score_artifact_id INTEGER PRIMARY KEY,
                schema_version TEXT NOT NULL
                    CHECK (schema_version = '1.0'),
                artifact_type TEXT NOT NULL
                    CHECK (
                        artifact_type =
                            'current-selection-joint-scenario-score-shadow'
                    ),
                artifact_version TEXT NOT NULL
                    CHECK (
                        artifact_version =
                            'current-selection-scenario-score-v1'
                    ),
                status TEXT NOT NULL
                    CHECK (status = 'prospective-shadow-unscored'),
                scenario_artifact_id INTEGER NOT NULL
                    REFERENCES joint_scenario_shadow_artifacts(
                        scenario_artifact_id
                    )
                    ON DELETE RESTRICT,
                forecast_artifact_id INTEGER NOT NULL
                    REFERENCES baseline_forecast_artifacts(artifact_id)
                    ON DELETE RESTRICT,
                selection_revision_id INTEGER
                    REFERENCES selection_revisions(selection_revision_id)
                    ON DELETE RESTRICT,
                user_selection_key TEXT NOT NULL
                    CHECK (
                        user_selection_key = 'missing'
                        OR length(user_selection_key) = 64
                    ),
                season_code TEXT NOT NULL CHECK (season_code = '2026-27'),
                gameweek INTEGER NOT NULL CHECK (gameweek = 1),
                decision_cutoff_utc TEXT NOT NULL,
                scenario_count INTEGER NOT NULL
                    CHECK (scenario_count BETWEEN 1 AND 512),
                producer_run_identity_sha256 TEXT NOT NULL
                    CHECK (length(producer_run_identity_sha256) = 64),
                document_json TEXT NOT NULL
                    CHECK (length(document_json) BETWEEN 2 AND 2097152),
                content_sha256 TEXT NOT NULL UNIQUE
                    CHECK (length(content_sha256) = 64),
                created_at_utc TEXT NOT NULL,
                UNIQUE (
                    scenario_artifact_id,
                    forecast_artifact_id,
                    user_selection_key
                )
            );

            CREATE INDEX selection_scenario_score_shadow_latest_idx
                ON selection_scenario_score_shadow_artifacts (
                    season_code,
                    gameweek,
                    score_artifact_id DESC
                );

            CREATE TRIGGER selection_scenario_score_shadow_immutable
            BEFORE UPDATE ON selection_scenario_score_shadow_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'selection scenario score shadow artifacts are immutable'
                );
            END;

            CREATE TRIGGER selection_scenario_score_shadow_no_delete
            BEFORE DELETE ON selection_scenario_score_shadow_artifacts
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'selection scenario score shadow artifacts cannot be deleted'
                );
            END;
            """),
    ];
}
