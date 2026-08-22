using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed partial class ResearchSourceClaimExtractor
{
    public const string FfScoutExtractionVersion =
        "ffscout-claims/v3";
    public const string FfScoutLineupExtractionVersion =
        "ffscout-predicted-lineups/v1";
    public const string FfScoutLineupComplementExtractionVersion =
        "ffscout-predicted-lineup-complement/v1";
    public const string FfScoutAvailabilityExtractionVersion =
        "ffscout-availability/v1";
    public const string StraightredExtractionVersion =
        "straightred-consensus/v1";
    public const string PremierLeagueInjuryExtractionVersion =
        "premier-league-injuries/v1";
    public const string FfScoutSourceKey = "ffscout-predicted-lineups";
    public const string StraightredSourceKey =
        "straightred-lineup-consensus";
    public const string PremierLeagueInjurySourceKey =
        "premier-league-injuries";

    private static readonly IReadOnlyDictionary<string, string> TeamAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["brighton and hove albion"] = "brighton",
            ["afc bournemouth"] = "bournemouth",
            ["leeds united"] = "leeds",
            ["manchester city"] = "man city",
            ["manchester united"] = "man utd",
            ["newcastle united"] = "newcastle",
            ["nottingham forest"] = "nott m forest",
            ["tottenham hotspur"] = "spurs",
            ["west ham united"] = "west ham",
            ["wolverhampton wanderers"] = "wolves",
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
        if (StringComparer.Ordinal.Equals(
                snapshot.SourceKey,
                PremierLeagueInjurySourceKey))
        {
            return await ExtractPremierLeagueInjuriesAsync(
                retained,
                cancellationToken);
        }
        if (StringComparer.Ordinal.Equals(
                snapshot.SourceKey,
                StraightredSourceKey))
        {
            return await ExtractStraightredAsync(
                retained,
                cancellationToken);
        }
        if (!StringComparer.Ordinal.Equals(
                snapshot.SourceKey,
                FfScoutSourceKey))
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
        IReadOnlyList<ResolvedLineupCandidate> resolvedCandidates = candidates
            .Select(candidate =>
                new ResolvedLineupCandidate(
                    candidate,
                    ResolveLineupIdentity(
                        candidate,
                        identitiesByCode,
                        identities)))
            .ToArray();
        int[] unresolvedCodes = resolvedCandidates
            .Where(candidate => candidate.Match is null)
            .Select(candidate => candidate.Candidate.PlayerCode)
            .Distinct()
            .Order()
            .ToArray();

        var claims = new List<EvidenceClaimDocument>();
        foreach (ResolvedLineupCandidate resolved in resolvedCandidates)
        {
            if (resolved.Match is null)
            {
                continue;
            }
            LineupCandidate candidate = resolved.Candidate;
            ResearchOfficialPlayerIdentity identity = resolved.Match.Identity;

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
                        resolved.Match.ExtractionConfidence,
                        ComputeDuplicateClusterKey(
                            snapshot,
                            "start",
                            identity.PlayerCode)),
                    snapshot.IdentityCaptureId,
                    cancellationToken));
        }
        foreach (IGrouping<string, ResolvedLineupCandidate> team in
                 resolvedCandidates.GroupBy(
                     candidate => candidate.Candidate.TeamName,
                     StringComparer.Ordinal))
        {
            ResolvedLineupCandidate[] teamCandidates = team.ToArray();
            if (teamCandidates.Length != 11
                || teamCandidates.Any(candidate => candidate.Match is null))
            {
                continue;
            }

            ResearchOfficialPlayerIdentity[] predictedStarters = teamCandidates
                .Select(candidate => candidate.Match!.Identity)
                .ToArray();
            string[] officialTeamNames = predictedStarters
                .Select(identity => identity.TeamName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (officialTeamNames.Length != 1
                || predictedStarters
                    .Select(identity => identity.PlayerId)
                    .Distinct()
                    .Count() != 11)
            {
                continue;
            }

            HashSet<int> predictedStarterIds = predictedStarters
                .Select(identity => identity.PlayerId)
                .ToHashSet();
            decimal complementConfidence = teamCandidates
                .Min(candidate => candidate.Match!.ExtractionConfidence);
            foreach (ResearchOfficialPlayerIdentity identity in identities
                         .Where(identity =>
                             StringComparer.Ordinal.Equals(
                                 identity.TeamName,
                                 officialTeamNames[0])
                             && !predictedStarterIds.Contains(identity.PlayerId))
                         .OrderBy(identity => identity.PlayerId))
            {
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
                            "does-not-start",
                            null,
                            null,
                            null,
                            "model-forecast",
                            $"{team.Key} complete predicted XI omits: {identity.WebName}",
                            "deterministic",
                            FfScoutLineupComplementExtractionVersion,
                            complementConfidence,
                            ComputeDuplicateClusterKey(
                                snapshot,
                                "start",
                                identity.PlayerCode)),
                        snapshot.IdentityCaptureId,
                        cancellationToken));
            }
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
            unresolvedCodes.Length,
            availabilityCandidates.Count,
            availabilityClaimCount,
            unresolvedAvailabilityCount,
            claims.Count,
            unresolvedCodes);
    }

    private async Task<ResearchSourceClaimExtractionDocument>
        ExtractStraightredAsync(
            ResearchSourceSnapshotContent retained,
            CancellationToken cancellationToken)
    {
        ResearchSourceSnapshotDocument snapshot = retained.Snapshot;
        IReadOnlyList<ConsensusCandidate> candidates =
            ExtractStraightredCandidates(retained.Content);
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities =
            await _snapshotStore.GetPlayerIdentitiesAsync(
                snapshot.IdentityCaptureId,
                cancellationToken);

        int unresolvedStartCount = 0;
        var claims = new List<EvidenceClaimDocument>();
        foreach (ConsensusCandidate candidate in candidates)
        {
            AvailabilityIdentityMatch? match = ResolveTeamScopedIdentity(
                candidate.TeamName,
                candidate.PlayerName,
                identities);
            if (match is null)
            {
                unresolvedStartCount++;
                continue;
            }

            claims.Add(
                await _claimStore.ImportForIdentityCaptureAsync(
                    new EvidenceClaimImportRequest(
                        "1.0",
                        snapshot.SourceKey,
                        snapshot.CanonicalUrl,
                        "strAIghtred",
                        null,
                        snapshot.RetrievedAtUtc,
                        snapshot.AvailableAtUtc,
                        snapshot.ContentSha256,
                        snapshot.SourceRevision,
                        snapshot.SeasonCode,
                        snapshot.Gameweek,
                        match.Identity.PlayerId,
                        "start",
                        null,
                        "starts",
                        candidate.ForecastProbability,
                        null,
                        null,
                        "model-forecast",
                        candidate.SourceSpan,
                        "deterministic",
                        StraightredExtractionVersion,
                        match.ExtractionConfidence,
                        ComputeDuplicateClusterKey(
                            snapshot,
                            "start",
                            match.Identity.PlayerCode)),
                    snapshot.IdentityCaptureId,
                    cancellationToken));
        }

        return new(
            "1.0",
            snapshot.SnapshotId,
            snapshot.SourceKey,
            StraightredExtractionVersion,
            candidates.Count,
            claims.Count,
            unresolvedStartCount,
            0,
            0,
            0,
            claims.Count,
            []);
    }

    private async Task<ResearchSourceClaimExtractionDocument>
        ExtractPremierLeagueInjuriesAsync(
            ResearchSourceSnapshotContent retained,
            CancellationToken cancellationToken)
    {
        ResearchSourceSnapshotDocument snapshot = retained.Snapshot;
        IReadOnlyList<PremierLeagueInjuryCandidate> candidates =
            ExtractPremierLeagueInjuryCandidates(retained.Content);
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities =
            await _snapshotStore.GetPlayerIdentitiesAsync(
                snapshot.IdentityCaptureId,
                cancellationToken);

        int unresolvedAvailabilityCount = 0;
        var claims = new List<EvidenceClaimDocument>();
        foreach (PremierLeagueInjuryCandidate candidate in candidates)
        {
            AvailabilityIdentityMatch? match = ResolveTeamScopedIdentity(
                candidate.TeamName,
                candidate.PlayerName,
                identities);
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
                        "Premier League",
                        null,
                        snapshot.RetrievedAtUtc,
                        snapshot.AvailableAtUtc,
                        snapshot.ContentSha256,
                        snapshot.SourceRevision,
                        snapshot.SeasonCode,
                        snapshot.Gameweek,
                        match.Identity.PlayerId,
                        "availability",
                        "doubtful",
                        null,
                        null,
                        null,
                        null,
                        "reported",
                        candidate.SourceSpan,
                        "deterministic",
                        PremierLeagueInjuryExtractionVersion,
                        match.ExtractionConfidence,
                        ComputeDuplicateClusterKey(
                            snapshot,
                            "availability",
                            match.Identity.PlayerCode)),
                    snapshot.IdentityCaptureId,
                    cancellationToken));
        }

        return new(
            "1.0",
            snapshot.SnapshotId,
            snapshot.SourceKey,
            PremierLeagueInjuryExtractionVersion,
            candidates.Count,
            0,
            0,
            candidates.Count,
            claims.Count,
            unresolvedAvailabilityCount,
            claims.Count,
            []);
    }

    internal static IReadOnlyList<PremierLeagueInjuryCandidate>
        ExtractPremierLeagueInjuryCandidates(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        JsonDocumentOptions options = new()
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16,
            AllowDuplicateProperties = false,
        };

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, options);
        }
        catch (JsonException exception)
        {
            throw new ResearchSourceSnapshotException(
                "The Premier League injury payload was not valid JSON.",
                exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !ReadExactString(
                    root,
                    "schemaVersion",
                    "premier-league-injury-dom/v1")
                || !ReadExactString(
                    root,
                    "sourceUrl",
                    "https://www.premierleague.com/en/latest-player-injuries")
                || !TryReadBoundedString(
                    root,
                    "pageTitle",
                    1,
                    200,
                    out _)
                || !TryReadBoundedString(
                    root,
                    "renderedWidgetSha256",
                    64,
                    64,
                    out string renderedHash)
                || !renderedHash.All(character =>
                    character is >= '0' and <= '9'
                        or >= 'a' and <= 'f')
                || !root.TryGetProperty("clubs", out JsonElement clubs)
                || clubs.ValueKind != JsonValueKind.Array
                || clubs.GetArrayLength() != 20)
            {
                throw new ResearchSourceSnapshotException(
                    "The Premier League injury payload had an unsupported shape.");
            }

            var candidates = new List<PremierLeagueInjuryCandidate>();
            var teamNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement club in clubs.EnumerateArray())
            {
                if (club.ValueKind != JsonValueKind.Object
                    || !TryReadBoundedString(
                        club,
                        "teamName",
                        1,
                        100,
                        out string teamName)
                    || !teamNames.Add(Normalize(teamName))
                    || !club.TryGetProperty("rows", out JsonElement rows)
                    || rows.ValueKind != JsonValueKind.Array)
                {
                    throw new ResearchSourceSnapshotException(
                        "A Premier League injury club had an unsupported shape.");
                }

                var playerNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object
                        || !TryReadBoundedString(
                            row,
                            "playerName",
                            1,
                            120,
                            out string playerName)
                        || !playerNames.Add(Normalize(playerName))
                        // A club may list a player without disclosing the injury
                        // type. The claim is that the player is listed, so the
                        // absent type is carried through as empty rather than
                        // dropping the row or inventing a diagnosis.
                        || !TryReadBoundedString(
                            row,
                            "injury",
                            0,
                            160,
                            out string injury)
                        || !HasOptionalSafeUpdateUri(row))
                    {
                        throw new ResearchSourceSnapshotException(
                            "A Premier League injury row had an unsupported shape.");
                    }

                    string sourceSpan = injury.Length == 0
                        ? $"{teamName} injury list: {playerName}"
                        : $"{teamName} injury list: {playerName} — {injury}";
                    if (sourceSpan.Length > 500)
                    {
                        throw new ResearchSourceSnapshotException(
                            "A Premier League injury source span exceeded the evidence limit.");
                    }
                    candidates.Add(
                        new(
                            teamName,
                            playerName,
                            injury,
                            sourceSpan));
                }
            }

            if (candidates.Count is < 1 or > 200)
            {
                throw new ResearchSourceSnapshotException(
                    "The Premier League injury payload contained an unsupported candidate count.");
            }

            return candidates;
        }
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
                        teamName,
                        playerName,
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

    internal static IReadOnlyList<ConsensusCandidate>
        ExtractStraightredCandidates(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string[] lines = content.Split('\n');
        int markerIndex = Array.FindIndex(
            lines,
            line => line.Contains(
                "Powered by multiple prediction sources.",
                StringComparison.Ordinal));
        if (markerIndex < 0)
        {
            throw new ResearchSourceSnapshotException(
                "The strAIghtred consensus content marker was not found.");
        }

        int fixtureIndex = Array.FindIndex(
            lines,
            markerIndex + 1,
            line => line.Contains(" vs ", StringComparison.Ordinal)
                && line.Contains('·'));
        if (fixtureIndex < 0)
        {
            throw new ResearchSourceSnapshotException(
                "The strAIghtred consensus fixture marker was not found.");
        }

        string fixture = lines[fixtureIndex].Trim().TrimEnd('\r');
        int versus = fixture.IndexOf(" vs ", StringComparison.Ordinal);
        int sourceSeparator = fixture.LastIndexOf('·');
        if (versus <= 0 || sourceSeparator <= versus + 4)
        {
            throw new ResearchSourceSnapshotException(
                "The strAIghtred consensus fixture had an unsupported shape.");
        }
        string homeTeam = TrimToLetters(fixture[..versus]);
        string awayTeam = TrimToLetters(
            fixture[(versus + 4)..sourceSeparator]);
        Match sourceCountMatch = ConsensusSourceCountRegex().Match(
            fixture[(sourceSeparator + 1)..].Trim());
        if (!sourceCountMatch.Success
            || !int.TryParse(
                sourceCountMatch.Groups["count"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int sourceCount)
            || sourceCount is < 1 or > 100)
        {
            throw new ResearchSourceSnapshotException(
                "The strAIghtred consensus source count was invalid.");
        }

        var candidates = new List<ConsensusCandidate>();
        for (int index = fixtureIndex + 1; index < lines.Length;)
        {
            string line = lines[index].Trim().TrimEnd('\r');
            if (line is "")
            {
                index++;
                continue;
            }
            if (ConsensusFormationRegex().IsMatch(line))
            {
                break;
            }
            Match nameMatch = FfScoutAvailabilityNameRegex().Match(line);
            if (!nameMatch.Success || index + 1 >= lines.Length)
            {
                throw new ResearchSourceSnapshotException(
                    "A strAIghtred consensus player had an unsupported shape.");
            }

            string probabilityLine = lines[index + 1].Trim().TrimEnd('\r');
            Match probabilityMatch =
                ConsensusProbabilityRegex().Match(probabilityLine);
            if (!probabilityMatch.Success
                || !int.TryParse(
                    probabilityMatch.Groups["percent"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int percent)
                || percent is < 0 or > 100)
            {
                throw new ResearchSourceSnapshotException(
                    "A strAIghtred consensus probability was invalid.");
            }

            string playerName = nameMatch.Groups["name"].Value.Trim();
            string sourceSpan =
                $"{homeTeam} consensus ({sourceCount} sources): "
                + $"{playerName} {percent}%";
            if (sourceSpan.Length > 500)
            {
                throw new ResearchSourceSnapshotException(
                    "A strAIghtred consensus source span exceeded the evidence limit.");
            }
            candidates.Add(
                new(
                    homeTeam,
                    awayTeam,
                    sourceCount,
                    playerName,
                    percent / 100m,
                    sourceSpan));
            index += 2;
        }

        if (candidates.Count is < 1 or > 11)
        {
            throw new ResearchSourceSnapshotException(
                "The strAIghtred snapshot did not contain a bounded consensus XI.");
        }
        if (candidates
            .Select(candidate => Normalize(candidate.PlayerName))
            .Distinct(StringComparer.Ordinal)
            .Count() != candidates.Count)
        {
            throw new ResearchSourceSnapshotException(
                "The strAIghtred snapshot contained duplicate consensus players.");
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
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities) =>
        ResolveTeamScopedIdentity(
            candidate.TeamName,
            candidate.PlayerName,
            identities);

    private static AvailabilityIdentityMatch? ResolveLineupIdentity(
        LineupCandidate candidate,
        IReadOnlyDictionary<int, ResearchOfficialPlayerIdentity> identitiesByCode,
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities)
    {
        if (identitiesByCode.TryGetValue(
                candidate.PlayerCode,
                out ResearchOfficialPlayerIdentity? identity)
            && TeamNamesMatch(candidate.TeamName, identity.TeamName))
        {
            return new(identity, 1m);
        }

        AvailabilityIdentityMatch? fallback = ResolveTeamScopedIdentity(
            candidate.TeamName,
            candidate.PlayerName,
            identities);
        return fallback is null
            ? null
            : fallback with
            {
                ExtractionConfidence =
                    Math.Min(0.95m, fallback.ExtractionConfidence),
            };
    }

    private static bool TeamNamesMatch(string sourceName, string officialName)
    {
        string normalizedSource = Normalize(sourceName);
        string expectedOfficial = TeamAliases.TryGetValue(
            normalizedSource,
            out string? alias)
            ? alias
            : normalizedSource;
        return StringComparer.Ordinal.Equals(
            expectedOfficial,
            Normalize(officialName));
    }

    private static AvailabilityIdentityMatch? ResolveTeamScopedIdentity(
        string teamName,
        string playerName,
        IReadOnlyList<ResearchOfficialPlayerIdentity> identities)
    {
        string sourceTeam = Normalize(teamName);
        string officialTeam = TeamAliases.TryGetValue(
            sourceTeam,
            out string? alias)
            ? alias
            : sourceTeam;
        string candidateName = Normalize(playerName);

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
        if (suffixMatches.Length == 1)
        {
            return new(suffixMatches[0], 0.95m);
        }
        if (suffixMatches.Length > 1
            || candidateName.Length < 4
            || candidateName.Contains(' '))
        {
            return null;
        }

        ResearchOfficialPlayerIdentity[] uniqueFirstNameTokenMatches =
            teamPlayers
                .Where(identity =>
                    Normalize(identity.FirstName)
                        .Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries)
                        .Contains(candidateName, StringComparer.Ordinal))
                .ToArray();
        return uniqueFirstNameTokenMatches.Length == 1
            ? new(uniqueFirstNameTokenMatches[0], 0.95m)
            : null;
    }

    private static bool ReadExactString(
        JsonElement element,
        string propertyName,
        string expected) =>
        element.TryGetProperty(propertyName, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
        && StringComparer.Ordinal.Equals(property.GetString(), expected);

    private static bool TryReadBoundedString(
        JsonElement element,
        string propertyName,
        int minimumLength,
        int maximumLength,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length >= minimumLength
            && value.Length <= maximumLength
            && !value.Any(char.IsControl);
    }

    private static bool HasOptionalSafeUpdateUri(JsonElement row)
    {
        if (!row.TryGetProperty("updateUrl", out JsonElement updateUrl))
        {
            return false;
        }
        if (updateUrl.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (updateUrl.ValueKind != JsonValueKind.String
            || !TryReadBoundedString(
                row,
                "updateUrl",
                1,
                2048,
                out string value)
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static string TrimToLetters(string value)
    {
        int first = 0;
        while (first < value.Length && !char.IsLetter(value[first]))
        {
            first++;
        }
        int last = value.Length - 1;
        while (last >= first && !char.IsLetterOrDigit(value[last]))
        {
            last--;
        }
        if (first > last)
        {
            throw new ResearchSourceSnapshotException(
                "A consensus team name was empty.");
        }
        return value[first..(last + 1)].Trim();
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

    [GeneratedRegex(
        @"^(?<count>[0-9]{1,3})\s+sources?$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConsensusSourceCountRegex();

    [GeneratedRegex(
        @"^(?<percent>[0-9]{1,3})%$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConsensusProbabilityRegex();

    [GeneratedRegex(
        @"^[0-9]{1,2}(?:-[0-9]{1,2}){2,4}$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConsensusFormationRegex();

    internal sealed record LineupCandidate(
        string TeamName,
        string PlayerName,
        int PlayerCode,
        string SourceSpan);

    internal sealed record AvailabilityCandidate(
        string TeamName,
        string PlayerName,
        string AvailabilityStatus,
        decimal? ForecastProbability,
        string SourceSpan);

    internal sealed record ConsensusCandidate(
        string TeamName,
        string OpponentName,
        int SourceCount,
        string PlayerName,
        decimal ForecastProbability,
        string SourceSpan);

    internal sealed record PremierLeagueInjuryCandidate(
        string TeamName,
        string PlayerName,
        string Injury,
        string SourceSpan);

    private sealed record AvailabilityIdentityMatch(
        ResearchOfficialPlayerIdentity Identity,
        decimal ExtractionConfidence);

    private sealed record ResolvedLineupCandidate(
        LineupCandidate Candidate,
        AvailabilityIdentityMatch? Match);
}
