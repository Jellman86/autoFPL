using Microsoft.Data.Sqlite;

namespace AutoFpl.Api.Persistence;

public sealed record DatabaseOptions
{
    public const int BusyTimeoutSeconds = 5;

    private DatabaseOptions(string databasePath)
    {
        DatabasePath = databasePath;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = BusyTimeoutSeconds,
        }.ToString();
    }

    public string DatabasePath { get; }

    public string ConnectionString { get; }

    public static DatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        bool runsInContainer = StringComparer.OrdinalIgnoreCase.Equals(
            configuration["DOTNET_RUNNING_IN_CONTAINER"],
            "true");
        string configuredPath = configuration["AutoFpl:DatabasePath"]
            ?? (runsInContainer
                ? "/data/autofpl.db"
                : Path.Combine(AppContext.BaseDirectory, "data", "autofpl.db"));
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("AutoFpl:DatabasePath cannot be empty.");
        }

        string databasePath = Path.GetFullPath(configuredPath);
        string? directory = Path.GetDirectoryName(databasePath);
        if (directory is null)
        {
            throw new InvalidOperationException("AutoFpl:DatabasePath must identify a database file.");
        }

        Directory.CreateDirectory(directory);
        return new(databasePath);
    }
}
