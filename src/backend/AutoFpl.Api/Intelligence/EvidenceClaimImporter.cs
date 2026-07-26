using System.Text.Json;
using System.Text.Json.Serialization;

using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class EvidenceClaimImporter
{
    public const int MaximumInputBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly EvidenceClaimStore _store;

    public EvidenceClaimImporter(EvidenceClaimStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<EvidenceClaimDocument> ImportFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The evidence claim file does not exist.",
                fullPath);
        }
        if (file.Length is <= 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Evidence claim files must contain 1 to {MaximumInputBytes} bytes.");
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        EvidenceClaimImportRequest request =
            await JsonSerializer.DeserializeAsync<EvidenceClaimImportRequest>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new JsonException("The evidence claim document cannot be null.");
        if (stream.Position > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Evidence claim files must contain at most {MaximumInputBytes} bytes.");
        }

        return await _store.ImportAsync(request, cancellationToken);
    }
}
