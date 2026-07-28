using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class FbrefPlayerMatchLogImporter
{
    public const string SourceKeyPrefix = "fbref-player-match-log-";
    public const string SourceKeySuffix = "-2025-26";

    private readonly FbrefPlayingTimeExtractor _playingTimeExtractor;
    private readonly ByparrClient _byparrClient;
    private readonly ResearchSourceSnapshotStore _snapshotStore;

    public FbrefPlayerMatchLogImporter(
        FbrefPlayingTimeExtractor playingTimeExtractor,
        ByparrClient byparrClient,
        ResearchSourceSnapshotStore snapshotStore)
    {
        _playingTimeExtractor = playingTimeExtractor
            ?? throw new ArgumentNullException(nameof(playingTimeExtractor));
        _byparrClient =
            byparrClient ?? throw new ArgumentNullException(nameof(byparrClient));
        _snapshotStore = snapshotStore
            ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public async Task<ResearchSourceSnapshotDocument> ImportAsync(
        int officialPlayerCode,
        CancellationToken cancellationToken = default)
    {
        if (officialPlayerCode <= 0)
        {
            throw Invalid("The official player code must be positive.");
        }

        FbrefPlayingTimeDocument playingTime =
            await _playingTimeExtractor.GetAsync(
                FbrefPlayerIdentityBridge.ReviewedSnapshotId,
                cancellationToken)
            ?? throw Invalid(
                "The reviewed FBref playing-time snapshot is not available.");
        return await ImportAsync(
            playingTime,
            officialPlayerCode,
            cancellationToken);
    }

    internal async Task<ResearchSourceSnapshotDocument> ImportAsync(
        FbrefPlayingTimeDocument playingTime,
        int officialPlayerCode,
        CancellationToken cancellationToken = default)
    {
        if (officialPlayerCode <= 0)
        {
            throw Invalid("The official player code must be positive.");
        }

        FbrefPlayingTimePlayerDocument player = ResolveReviewedPlayer(
            playingTime,
            officialPlayerCode);
        ResearchSourceDefinition source = CreateSourceDefinition(player);
        ByparrCaptureResult capture =
            await _byparrClient.CaptureAsync(source, cancellationToken);
        return await _snapshotStore.PersistAsync(
            source,
            capture,
            cancellationToken);
    }

    internal static FbrefPlayingTimePlayerDocument ResolveReviewedPlayer(
        FbrefPlayingTimeDocument playingTime,
        int officialPlayerCode)
    {
        ArgumentNullException.ThrowIfNull(playingTime);
        if (!StringComparer.Ordinal.Equals(
                playingTime.IdentityBridgeVersion,
                FbrefPlayerIdentityBridge.Version)
            || playingTime.SnapshotId
                != FbrefPlayerIdentityBridge.ReviewedSnapshotId
            || !StringComparer.Ordinal.Equals(
                playingTime.ContentSha256,
                FbrefPlayerIdentityBridge.ReviewedContentSha256))
        {
            throw Invalid(
                "Player match logs require the exact reviewed FBref identity bridge.");
        }

        FbrefPlayingTimePlayerDocument[] matches = playingTime.Players
            .Where(player =>
                player.OfficialPlayerCode == officialPlayerCode
                && StringComparer.Ordinal.Equals(
                    player.IdentityStatus,
                    "reviewed-v1"))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Invalid(
                $"Official player code {officialPlayerCode} is not a unique reviewed FBref identity.");
    }

    internal static ResearchSourceDefinition CreateSourceDefinition(
        FbrefPlayingTimePlayerDocument player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!IsLowerHexId(player.SourcePlayerId)
            || player.OfficialPlayerCode is null
            || !StringComparer.Ordinal.Equals(
                player.IdentityStatus,
                "reviewed-v1")
            || !Uri.TryCreate(
                player.MatchLogsUrl,
                UriKind.Absolute,
                out Uri? canonicalUri)
            || !StringComparer.Ordinal.Equals(canonicalUri.Scheme, "https")
            || !StringComparer.OrdinalIgnoreCase.Equals(
                canonicalUri.Host,
                "fbref.com")
            || canonicalUri.Port != 443
            || !string.IsNullOrEmpty(canonicalUri.UserInfo)
            || !string.IsNullOrEmpty(canonicalUri.Query)
            || !string.IsNullOrEmpty(canonicalUri.Fragment))
        {
            throw Invalid("The reviewed player has an invalid FBref match-log identity.");
        }

        string expectedPrefix =
            $"/en/players/{player.SourcePlayerId}/matchlogs/2025-2026/summary/";
        if (!canonicalUri.AbsolutePath.StartsWith(
                expectedPrefix,
                StringComparison.Ordinal)
            || canonicalUri.AbsolutePath.Length <= expectedPrefix.Length)
        {
            throw Invalid("The reviewed player has an invalid FBref match-log URL.");
        }

        var finalUriBuilder = new UriBuilder(canonicalUri)
        {
            Path = canonicalUri.AbsolutePath.Replace(
                "/2025-2026/summary/",
                "/2025-2026/",
                StringComparison.Ordinal),
        };
        return new(
            $"{SourceKeyPrefix}{player.SourcePlayerId}{SourceKeySuffix}",
            "prior-competition-player-match-log",
            canonicalUri,
            "sports-reference-fbref",
            [
                "match-date",
                "competition",
                "opponent",
                "start",
                "minutes",
                "performance",
            ],
            true,
            [
                "Capture is restricted to a reviewed source-to-official player identity.",
                "The page is retrospective evidence and cannot establish future availability.",
                "Rows require point-in-time filtering before any model feature is formed.",
                "The source remains shadow-only until identical-fold predictive gain is measured.",
            ],
            ByparrClient.TransportKey,
            [finalUriBuilder.Uri],
            [
                "Match Logs | FBref.com",
                "id=\"matchlogs_all\"",
            ],
            PollAutomatically: false);
    }

    public static bool IsMatchLogSourceKey(string sourceKey) =>
        sourceKey.Length == SourceKeyPrefix.Length + 8 + SourceKeySuffix.Length
        && sourceKey.StartsWith(SourceKeyPrefix, StringComparison.Ordinal)
        && sourceKey.EndsWith(SourceKeySuffix, StringComparison.Ordinal)
        && IsLowerHexId(
            sourceKey.Substring(SourceKeyPrefix.Length, 8));

    private static bool IsLowerHexId(string value) =>
        value.Length == 8
        && value.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private static ResearchSourceSnapshotException Invalid(string message) =>
        new(message);
}
