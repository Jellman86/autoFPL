using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class ResearchSourceSnapshotImporter
{
    private readonly SpiderMcpClient _spiderClient;
    private readonly ByparrClient? _byparrClient;
    private readonly PremierLeagueInjuryPlaywrightCollector?
        _premierLeagueInjuryCollector;
    private readonly ResearchSourceSnapshotStore _store;

    public ResearchSourceSnapshotImporter(
        SpiderMcpClient spiderClient,
        ByparrClient byparrClient,
        ResearchSourceSnapshotStore store,
        PremierLeagueInjuryPlaywrightCollector? premierLeagueInjuryCollector = null)
    {
        _spiderClient =
            spiderClient ?? throw new ArgumentNullException(nameof(spiderClient));
        _byparrClient =
            byparrClient ?? throw new ArgumentNullException(nameof(byparrClient));
        _premierLeagueInjuryCollector =
            premierLeagueInjuryCollector;
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    internal ResearchSourceSnapshotImporter(
        SpiderMcpClient spiderClient,
        ResearchSourceSnapshotStore store,
        PremierLeagueInjuryPlaywrightCollector? premierLeagueInjuryCollector = null)
    {
        _spiderClient =
            spiderClient ?? throw new ArgumentNullException(nameof(spiderClient));
        _premierLeagueInjuryCollector = premierLeagueInjuryCollector;
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ResearchSourceSnapshotDocument> ImportAsync(
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        ResearchSourceDefinition source = ResearchSourceRegistry.Get(sourceKey);
        if (StringComparer.Ordinal.Equals(
                source.TransportKey,
                ByparrClient.TransportKey))
        {
            ByparrClient client = _byparrClient
                ?? throw new ResearchSourceSnapshotException(
                    "The registered Byparr source requires a configured Byparr transport.");
            ByparrCaptureResult capture =
                await client.CaptureAsync(source, cancellationToken);
            return await _store.PersistAsync(
                source,
                capture,
                cancellationToken);
        }

        if (StringComparer.Ordinal.Equals(
                source.TransportKey,
                PremierLeagueInjuryPlaywrightCollector.TransportKey))
        {
            PremierLeagueInjuryPlaywrightCollector client =
                _premierLeagueInjuryCollector
                ?? throw new ResearchSourceSnapshotException(
                    "The registered Premier League injury source requires Playwright MCP.");
            PlaywrightResearchSourceCaptureResult capture =
                await client.CaptureAsync(source, cancellationToken);
            return await _store.PersistAsync(
                source,
                capture,
                cancellationToken);
        }

        if (!StringComparer.Ordinal.Equals(
                source.TransportKey,
                SpiderMcpClient.TransportKey))
        {
            throw new ResearchSourceSnapshotException(
                $"Unsupported research transport: {source.TransportKey}");
        }

        SpiderScrapeResult scrape =
            await _spiderClient.ScrapeAsync(source, cancellationToken);
        return await _store.PersistAsync(
            source,
            scrape,
            cancellationToken);
    }
}
