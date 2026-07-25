namespace AutoFpl.Api.Persistence;

internal sealed record DatabaseMigration(int Version, string Name, string Sql);

internal static class DatabaseMigrations
{
    public const int CurrentVersion = 3;

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
    ];
}
