using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed partial class ResearchSourceClaimExtractor
{
    public const string FfScoutExtractionVersion =
        "ffscout-claims/v2";
    public const string FfScoutLineupExtractionVersion =
        "ffscout-predicted-lineups/v1";
    public const string FfScoutAvailabilityExtractionVersion =
        "ffscout-availability/v1";

    private const string FfScoutSourceKey = "ffscout-predicted-lineups";

    private static readonly IReadOnlyDictionary<string, string> FfScoutTeamAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["brighton and hove albion"] = "brighton",
            ["leeds united"] = "leeds",
            ["manchester city"] = "man city",
            ["manchester united"] = "man utd",
            ["newcastle united"] = "newcastle",
            ["nottingham forest"] = "nott m forest",
            ["tottenham hotspur"] = "spurs",
        };

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
        IReadOnlyList<AvailabilityCandidate> availabilityCandidates =
            ExtractFfScoutAvailabilityCandidates(retained.Content);
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities =
            await _snapshotStore.GetPlayerIdentitiesAsync(
                snapshot.IdentityCaptureId,
                cancellationToken);
        IReadOnlyDictionary<int, ResearchOfficialPlayerIdentity> identitiesByCode =
            identities.ToDictionary(identity => identity.PlayerCode);
        int[] unresolvedCodes = candidates
            .Where(candidate =>
                !identitiesByCode.ContainsKey(candidate.PlayerCode))
            .Select(candidate => candidate.PlayerCode)
            .Distinct()
            .Order()
            .ToArray();

        var claims = new List<EvidenceClaimDocument>();
        foreach (LineupCandidate candidate in candidates)
        {
            if (!identitiesByCode.TryGetValue(
                    candidate.PlayerCode,
                    out ResearchOfficialPlayerIdentity? identity))
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
                        identity.PlayerId,
                        "start",
                        null,
                        "starts",
                        null,
                        null,
                        null,
                        "model-forecast",
                        candidate.SourceSpan,
                        "deterministic",
                        FfScoutLineupExtractionVersion,
                        1m,
                        ComputeDuplicateClusterKey(
                            snapshot,
                            "start",
                            identity.PlayerCode)),
                    snapshot.IdentityCaptureId,
                    cancellationToken));
        }
        int startClaimCount = claims.Count;

        int unresolvedAvailabilityCount = 0;
        foreach (AvailabilityCandidate candidate in availabilityCandidates)
        {
            AvailabilityIdentityMatch? match =
                ResolveAvailabilityIdentity(candidate, identities);
            if (match is null)
            {
                unresolvedAvailabilityCount++;
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
                        match.Identity.PlayerId,
                        "availability",
                        candidate.AvailabilityStatus,
                        null,
                        candidate.ForecastProbability,
                        null,
                        null,
                        "reported",
                        candidate.SourceSpan,
                        "deterministic",
                        FfScoutAvailabilityExtractionVersion,
                        match.ExtractionConfidence,
                        ComputeDuplicateClusterKey(
                            snapshot,
                            "availability",
                            match.Identity.PlayerCode)),
                    snapshot.IdentityCaptureId,
                    cancellationToken));
        }
        int availabilityClaimCount = claims.Count - startClaimCount;

        return new(
            "1.0",
            snapshot.SnapshotId,
            snapshot.SourceKey,
            FfScoutExtractionVersion,
            candidates.Count,
            startClaimCount,
            availabilityCandidates.Count,
            availabilityClaimCount,
            unresolvedAvailabilityCount,
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

    internal static IReadOnlyList<AvailabilityCandidate>
        ExtractFfScoutAvailabilityCandidates(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Match introduction = FfScoutIntroductionRegex().Match(content);
        if (!introduction.Success)
        {
            throw new ResearchSourceSnapshotException(
                "The FFScout predicted-lineup content marker was not found.");
        }

        string lineupContent = content[introduction.Index..];
        var candidates = new List<AvailabilityCandidate>();
        foreach (Match teamMatch in FfScoutTeamRegex().Matches(lineupContent))
        {
            string teamName = teamMatch.Groups["team"].Value.Trim();
            string body = teamMatch.Groups["body"].Value;
            AddAvailabilitySection(
                candidates,
                teamName,
                body,
                "* **Out:**",
                "* **Doubts:**",
                "unavailable",
                hasProbability: false);
            AddAvailabilitySection(
                candidates,
                teamName,
                body,
                "* **Doubts:**",
                "* **Banned:**",
                "doubtful",
                hasProbability: true);
        }

        if (candidates.Count > 100)
        {
            throw new ResearchSourceSnapshotException(
                "The FFScout snapshot exceeded the bounded availability candidate count.");
        }
        if (candidates
            .Select(candidate =>
                $"{Normalize(candidate.TeamName)}|"
                + $"{Normalize(candidate.PlayerName)}|"
                + candidate.AvailabilityStatus)
            .Distinct(StringComparer.Ordinal)
            .Count() != candidates.Count)
        {
            throw new ResearchSourceSnapshotException(
                "The FFScout snapshot contained duplicate availability candidates.");
        }

        return candidates;
    }

    private static void AddAvailabilitySection(
        ICollection<AvailabilityCandidate> candidates,
        string teamName,
        string body,
        string startMarker,
        string endMarker,
        string availabilityStatus,
        bool hasProbability)
    {
        int start = body.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return;
        }
        start += startMarker.Length;
        int end = body.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new ResearchSourceSnapshotException(
                "An FFScout availability section was not terminated.");
        }

        string section = body[start..end];
        foreach (string rawLine in section.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (line is "" or "*")
            {
                continue;
            }
            if (line.StartsWith("* ", StringComparison.Ordinal))
            {
                line = line[2..].Trim();
            }

            Match match = hasProbability
                ? FfScoutDoubtRegex().Match(line)
                : FfScoutAvailabilityNameRegex().Match(line);
            if (!match.Success)
            {
                throw new ResearchSourceSnapshotException(
                    "An FFScout availability entry had an unsupported shape.");
            }

            string playerName = match.Groups["name"].Value.Trim();
            decimal? forecastProbability = null;
            if (hasProbability)
            {
                if (!int.TryParse(
                        match.Groups["percent"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int percent)
                    || percent is < 0 or > 100)
                {
                    throw new ResearchSourceSnapshotException(
                        "An FFScout doubt percentage was invalid.");
                }
                forecastProbability = percent / 100m;
            }

            string sourceSpan = hasProbability
                ? $"{teamName} Doubts: {playerName} "
                    + $"{match.Groups["percent"].Value}%"
                : $"{teamName} Out: {playerName}";
            if (sourceSpan.Length > 500)
            {
                throw new ResearchSourceSnapshotException(
                    "An FFScout availability source span exceeded the evidence limit.");
            }
            candidates.Add(
                new(
                    teamName,
                    playerName,
                    availabilityStatus,
                    forecastProbability,
                    sourceSpan));
        }
    }

    private static AvailabilityIdentityMatch? ResolveAvailabilityIdentity(
        AvailabilityCandidate candidate,
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities)
    {
        string sourceTeam = Normalize(candidate.TeamName);
        string officialTeam = FfScoutTeamAliases.TryGetValue(
            sourceTeam,
            out string? alias)
            ? alias
            : sourceTeam;
        string candidateName = Normalize(candidate.PlayerName);

        ResearchOfficialPlayerIdentity[] teamPlayers = identities
            .Where(identity =>
                StringComparer.Ordinal.Equals(
                    Normalize(identity.TeamName),
                    officialTeam))
            .ToArray();
        ResearchOfficialPlayerIdentity[] exactMatches = teamPlayers
            .Where(identity => OfficialNames(identity).Contains(candidateName))
            .ToArray();
        if (exactMatches.Length == 1)
        {
            return new(exactMatches[0], 1m);
        }
        if (exactMatches.Length > 1)
        {
            return null;
        }

        ResearchOfficialPlayerIdentity[] suffixMatches = teamPlayers
            .Where(
                identity =>
                {
                    string webName = Normalize(identity.WebName);
                    return webName.Length >= 4
                        && candidateName.EndsWith(
                            $" {webName}",
                            StringComparison.Ordinal);
                })
            .ToArray();
        return suffixMatches.Length == 1
            ? new(suffixMatches[0], 0.95m)
            : null;
    }

    private static HashSet<string> OfficialNames(
        ResearchOfficialPlayerIdentity identity) =>
        new(StringComparer.Ordinal)
        {
            Normalize(identity.WebName),
            Normalize(identity.SecondName),
            Normalize($"{identity.FirstName} {identity.SecondName}"),
        };

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder(decomposed.Length);
        bool lastWasSpace = true;
        foreach (char character in decomposed)
        {
            UnicodeCategory category =
                CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(character))
            {
                output.Append(char.ToLowerInvariant(character));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                output.Append(' ');
                lastWasSpace = true;
            }
        }

        return output.ToString().Trim();
    }

    private static string ComputeDuplicateClusterKey(
        ResearchSourceSnapshotDocument snapshot,
        string claimType,
        int playerCode)
    {
        string canonical =
            $"{claimType}|{snapshot.SeasonCode}|{snapshot.Gameweek}|{playerCode}";
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

    [GeneratedRegex(
        @"^(?<name>[\p{L}\p{M} .'’\-]+)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FfScoutAvailabilityNameRegex();

    [GeneratedRegex(
        @"^(?<name>[\p{L}\p{M} .'’\-]+?)\s+(?<percent>[0-9]{1,3})%$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FfScoutDoubtRegex();

    internal sealed record LineupCandidate(int PlayerCode, string SourceSpan);

    internal sealed record AvailabilityCandidate(
        string TeamName,
        string PlayerName,
        string AvailabilityStatus,
        decimal? ForecastProbability,
        string SourceSpan);

    private sealed record AvailabilityIdentityMatch(
        ResearchOfficialPlayerIdentity Identity,
        decimal ExtractionConfidence);
}
