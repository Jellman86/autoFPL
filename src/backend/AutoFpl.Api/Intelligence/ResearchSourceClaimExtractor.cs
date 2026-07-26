using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed partial class ResearchSourceClaimExtractor
{
    public const string FfScoutExtractionVersion =
        "ffscout-predicted-lineups/v1";

    private const string FfScoutSourceKey = "ffscout-predicted-lineups";

    private readonly ResearchSourceSnapshotStore _snapshotStore;
    private readonly EvidenceClaimStore _claimStore;

    public ResearchSourceClaimExtractor(
        ResearchSourceSnapshotStore snapshotStore,
        EvidenceClaimStore claimStore)
    {
        _snapshotStore =
            snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _claimStore =
            claimStore ?? throw new ArgumentNullException(nameof(claimStore));
    }

    public async Task<ResearchSourceClaimExtractionDocument> ExtractAsync(
        long snapshotId,
        CancellationToken cancellationToken = default)
    {
        ResearchSourceSnapshotContent retained =
            await _snapshotStore.ReadContentAsync(snapshotId, cancellationToken)
            ?? throw new ResearchSourceSnapshotException(
                $"Research source snapshot {snapshotId} was not found.");
        ResearchSourceSnapshotDocument snapshot = retained.Snapshot;
        if (!snapshot.IsPreDeadline)
        {
            throw new ResearchSourceSnapshotException(
                "Post-deadline research snapshots cannot produce evidence claims.");
        }
        if (!StringComparer.Ordinal.Equals(snapshot.Status, "shadow-only"))
        {
            throw new ResearchSourceSnapshotException(
                "Only shadow-only research snapshots can produce quarantined claims.");
        }
        if (!StringComparer.Ordinal.Equals(snapshot.SourceKey, FfScoutSourceKey))
        {
            throw new ResearchSourceSnapshotException(
                $"No deterministic claim extractor is registered for {snapshot.SourceKey}.");
        }

        IReadOnlyList<LineupCandidate> candidates =
            ExtractFfScoutLineupCandidates(retained.Content);
        IReadOnlyDictionary<int, int> identities =
            await _snapshotStore.GetPlayerIdsByCodeAsync(
                snapshot.IdentityCaptureId,
                cancellationToken);
        int[] unresolvedCodes = candidates
            .Where(candidate => !identities.ContainsKey(candidate.PlayerCode))
            .Select(candidate => candidate.PlayerCode)
            .Distinct()
            .Order()
            .ToArray();

        var claims = new List<EvidenceClaimDocument>();
        foreach (LineupCandidate candidate in candidates)
        {
            if (!identities.TryGetValue(candidate.PlayerCode, out int playerId))
            {
                continue;
            }

            claims.Add(
                await _claimStore.ImportForIdentityCaptureAsync(
                    new EvidenceClaimImportRequest(
                        "1.0",
                        snapshot.SourceKey,
                        snapshot.CanonicalUrl,
                        "Fantasy Football Scout",
                        null,
                        snapshot.RetrievedAtUtc,
                        snapshot.AvailableAtUtc,
                        snapshot.ContentSha256,
                        snapshot.SourceRevision,
                        snapshot.SeasonCode,
                        snapshot.Gameweek,
                        playerId,
                        "start",
                        null,
                        "starts",
                        null,
                        null,
                        null,
                        "model-forecast",
                        candidate.SourceSpan,
                        "deterministic",
                        FfScoutExtractionVersion,
                        1m,
                        ComputeDuplicateClusterKey(snapshot, candidate)),
                    snapshot.IdentityCaptureId,
                    cancellationToken));
        }

        return new(
            "1.0",
            snapshot.SnapshotId,
            snapshot.SourceKey,
            FfScoutExtractionVersion,
            candidates.Count,
            claims.Count,
            unresolvedCodes);
    }

    internal static IReadOnlyList<LineupCandidate> ExtractFfScoutLineupCandidates(
        string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Match introduction = FfScoutIntroductionRegex().Match(content);
        if (!introduction.Success)
        {
            throw new ResearchSourceSnapshotException(
                "The FFScout predicted-lineup content marker was not found.");
        }

        string lineupContent = content[introduction.Index..];
        var candidates = new List<LineupCandidate>();
        foreach (Match teamMatch in FfScoutTeamRegex().Matches(lineupContent))
        {
            string body = teamMatch.Groups["body"].Value;
            int injuryIndex = body.IndexOf("* **Out:**", StringComparison.Ordinal);
            if (injuryIndex < 0)
            {
                continue;
            }

            string predictedXi = body[..injuryIndex];
            MatchCollection playerMatches =
                FfScoutPlayerRegex().Matches(predictedXi);
            if (playerMatches.Count is < 1 or > 11)
            {
                throw new ResearchSourceSnapshotException(
                    "An FFScout team block did not contain between one and eleven predicted players.");
            }

            string teamName = teamMatch.Groups["team"].Value.Trim();
            foreach (Match playerMatch in playerMatches)
            {
                if (!int.TryParse(
                        playerMatch.Groups["code"].Value,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int playerCode)
                    || playerCode <= 0)
                {
                    throw new ResearchSourceSnapshotException(
                        "An FFScout predicted-lineup photo code was invalid.");
                }
                string playerName = playerMatch.Groups["name"].Value.Trim();
                string sourceSpan =
                    $"{teamName} predicted XI: {playerName}";
                if (sourceSpan.Length > 500)
                {
                    throw new ResearchSourceSnapshotException(
                        "An FFScout predicted-lineup source span exceeded the evidence limit.");
                }
                candidates.Add(
                    new(
                        playerCode,
                        sourceSpan));
            }
        }

        if (candidates.Count == 0)
        {
            throw new ResearchSourceSnapshotException(
                "The FFScout snapshot contained no deterministic predicted-lineup candidates.");
        }
        if (candidates.Select(candidate => candidate.PlayerCode).Distinct().Count()
            != candidates.Count)
        {
            throw new ResearchSourceSnapshotException(
                "The FFScout snapshot contained duplicate predicted player identities.");
        }
        if (candidates.Count > 220)
        {
            throw new ResearchSourceSnapshotException(
                "The FFScout snapshot exceeded the bounded predicted-lineup candidate count.");
        }

        return candidates;
    }

    private static string ComputeDuplicateClusterKey(
        ResearchSourceSnapshotDocument snapshot,
        LineupCandidate candidate)
    {
        string canonical =
            $"start|{snapshot.SeasonCode}|{snapshot.Gameweek}|{candidate.PlayerCode}";
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    [GeneratedRegex(
        @"Our Team News page will house predicted line-ups",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FfScoutIntroductionRegex();

    [GeneratedRegex(
        @"(?ms)^!\[[^\]\r\n]+ badge\]\([^\r\n]+\)##\r?\n(?<team>[^\r\n]+)\r?\n(?<body>.*?)(?=^!\[[^\]\r\n]+ badge\]\(|\z)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FfScoutTeamRegex();

    [GeneratedRegex(
        @"(?m)^\* !\[Avatar of [^\]\r\n]+\]\(https://resources\.premierleague\.com/[^\s)\r\n]*/(?<code>[0-9]+)\.png\)(?<name>[^\r\n]+)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FfScoutPlayerRegex();

    internal sealed record LineupCandidate(int PlayerCode, string SourceSpan);
}
