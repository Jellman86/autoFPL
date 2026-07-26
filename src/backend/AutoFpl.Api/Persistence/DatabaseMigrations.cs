namespace AutoFpl.Api.Persistence;

internal sealed record DatabaseMigration(int Version, string Name, string Sql);

internal static class DatabaseMigrations
{
    public const int CurrentVersion = 7;

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
    ];
}
