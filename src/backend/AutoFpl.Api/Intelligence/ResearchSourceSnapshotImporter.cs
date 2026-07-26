using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class ResearchSourceSnapshotImporter
{
    private readonly SpiderMcpClient _client;
    private readonly ResearchSourceSnapshotStore _store;

    public ResearchSourceSnapshotImporter(
        SpiderMcpClient client,
        ResearchSourceSnapshotStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ResearchSourceSnapshotDocument> ImportAsync(
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        ResearchSourceDefinition source = ResearchSourceRegistry.Get(sourceKey);
        SpiderScrapeResult scrape =
            await _client.ScrapeAsync(source, cancellationToken);
        return await _store.PersistAsync(source, scrape, cancellationToken);
    }
}
