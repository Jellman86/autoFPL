using System.Security.Cryptography;
using System.Text;

using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed class EvidenceSemanticReviewGenerator
{
    private readonly EvidenceSemanticReviewProviderOptions _options;
    private readonly CurrentEvidenceReviewContextStore _contextStore;
    private readonly EvidenceSemanticReviewStore _store;
    private readonly IEvidenceSemanticReviewProvider? _provider;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EvidenceSemanticReviewGenerator(
        EvidenceSemanticReviewProviderOptions options,
        CurrentEvidenceReviewContextStore contextStore,
        EvidenceSemanticReviewStore store,
        IEvidenceSemanticReviewProvider? provider,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provider = provider;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<EvidenceSemanticReviewGenerationResult> GenerateCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new("disabled", null);
        }
        if (_provider is null)
        {
            throw new InvalidOperationException(
                "The enabled semantic review generator requires a provider.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EvidenceReviewContextDocument? context =
                await _contextStore.GetCurrentAsync(cancellationToken);
            if (context is null)
            {
                return new("no-context", null);
            }
            EvidenceSemanticReviewDocument? current =
                await _store.GetCurrentAsync(cancellationToken);
            if (current is not null)
            {
                return new("current", current);
            }
            if (context.Targets.Count == 0)
            {
                return new("no-targets", null);
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (now < context.DecisionCutoffUtc)
            {
                return new("before-cutoff", null);
            }
            if (now >= context.DeadlineUtc)
            {
                return new("after-deadline", null);
            }

            DateTimeOffset requestedAtUtc = now;
            long started = _timeProvider.GetTimestamp();
            try
            {
                EvidenceSemanticReviewProviderResponse response =
                    await _provider.ReviewAsync(context, cancellationToken);
                EvidenceSemanticReviewDocument document = BuildDocument(
                    context,
                    response.Status,
                    response.Reason,
                    response.Results,
                    response.ProviderModel,
                    response.RequestedAtUtc,
                    response.CompletedAtUtc,
                    response.Outcome,
                    response.LatencyMilliseconds,
                    response.ResponseSha256);
                EvidenceSemanticReviewDocument stored = await _store.ImportAsync(
                    document,
                    cancellationToken);
                return new("generated", stored);
            }
            catch (EvidenceSemanticReviewProviderException exception)
            {
                return await PersistUnavailableAsync(
                    context,
                    requestedAtUtc,
                    exception.Outcome,
                    (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds,
                    cancellationToken);
            }
            catch (EvidenceSemanticReviewValidationException)
            {
                EvidenceReviewContextDocument? latest =
                    await _contextStore.GetCurrentAsync(cancellationToken);
                if (latest?.ContextIdentitySha256 != context.ContextIdentitySha256)
                {
                    return new("context-changed", null);
                }
                return await PersistUnavailableAsync(
                    context,
                    requestedAtUtc,
                    "invalid-provider-output",
                    (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return await PersistUnavailableAsync(
                    context,
                    requestedAtUtc,
                    "invalid-provider-output",
                    (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds,
                    cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<EvidenceSemanticReviewGenerationResult> PersistUnavailableAsync(
        EvidenceReviewContextDocument context,
        DateTimeOffset requestedAtUtc,
        string outcome,
        long latencyMilliseconds,
        CancellationToken cancellationToken)
    {
        DateTimeOffset completedAtUtc = _timeProvider.GetUtcNow();
        if (completedAtUtc > context.DeadlineUtc)
        {
            return new("failed-after-deadline", null);
        }
        EvidenceSemanticReviewDocument unavailable = BuildDocument(
            context,
            EvidenceSemanticReviewStore.StatusUnavailable,
            $"provider-{outcome}",
            [],
            _options.Model,
            requestedAtUtc,
            completedAtUtc,
            outcome,
            Math.Clamp(latencyMilliseconds, 0, 120_000),
            null);
        EvidenceSemanticReviewDocument stored = await _store.ImportAsync(
            unavailable,
            cancellationToken);
        return new("unavailable", stored);
    }

    private EvidenceSemanticReviewDocument BuildDocument(
        EvidenceReviewContextDocument context,
        string status,
        string? reason,
        IReadOnlyList<EvidenceSemanticReviewProviderResult> providerResults,
        string providerModel,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset completedAtUtc,
        string outcome,
        long latencyMilliseconds,
        string? responseSha256)
    {
        Dictionary<int, EvidenceReviewTargetDocument> targets = context.Targets
            .ToDictionary(target => target.Player.PlayerId);
        EvidenceSemanticReviewResultDocument[] results =
        [
            .. providerResults.Select(result =>
            {
                if (!targets.TryGetValue(result.PlayerId, out EvidenceReviewTargetDocument? target))
                {
                    throw new InvalidOperationException("Provider returned an unknown player.");
                }
                return new EvidenceSemanticReviewResultDocument(
                    target.Player,
                    result.Verdict,
                    result.EvidenceQuality,
                    result.TimeHorizon,
                    result.ScenarioKeys,
                    result.SupportingClaimIds,
                    result.ContradictingClaimIds,
                    result.DependentClaimIds,
                    result.CorroboratingSourceKeys,
                    result.ContradictingSourceKeys,
                    result.Assumptions,
                    result.Uncertainties,
                    result.Scope,
                    result.Rationale,
                    result.IsAbstained,
                    false,
                    null);
            }),
        ];
        EvidenceSemanticReviewCoverageDocument coverage = Coverage(context, results);
        string responseIdentity = responseSha256 ?? $"no-response:{outcome}";
        string dataIdentity = Sha256(
            $"{context.ContextIdentitySha256}\n{responseIdentity}\n{status}");
        string runIdentity = Sha256(
            $"{dataIdentity}\n{_options.Provider}\n{providerModel}\n"
            + $"{_options.RoutingPolicyVersion}\n{requestedAtUtc:O}\n{completedAtUtc:O}");

        return new(
            "1.0",
            EvidenceSemanticReviewStore.ArtifactType,
            EvidenceSemanticReviewStore.ArtifactVersion,
            status,
            reason,
            false,
            false,
            context.SeasonCode,
            context.Gameweek,
            context.DeadlineUtc,
            context.DecisionCutoffUtc,
            context.OfficialCaptureId,
            context.StressArtifactId,
            context.StressArtifactContentSha256,
            context.ContextIdentitySha256,
            _options.Provider,
            providerModel,
            context.ReviewPolicy.PromptVersion,
            context.ReviewPolicy.OutputSchemaVersion,
            requestedAtUtc,
            completedAtUtc,
            dataIdentity,
            runIdentity,
            coverage,
            results,
            null,
            null,
            OpenAiCompatibleEvidenceSemanticReviewProvider.AdapterName,
            _options.RoutingPolicyVersion,
            outcome,
            latencyMilliseconds,
            responseSha256);
    }

    private static EvidenceSemanticReviewCoverageDocument Coverage(
        EvidenceReviewContextDocument context,
        IReadOnlyList<EvidenceSemanticReviewResultDocument> results)
    {
        int citedClaims = results.SelectMany(result =>
                result.SupportingClaimIds
                    .Concat(result.ContradictingClaimIds)
                    .Concat(result.DependentClaimIds))
            .Distinct()
            .Count();
        int sources = results.SelectMany(result =>
                result.CorroboratingSourceKeys.Concat(result.ContradictingSourceKeys))
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new(
            context.Targets.Count,
            results.Count,
            results.Count(result => result.IsAbstained),
            0,
            citedClaims,
            sources);
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record EvidenceSemanticReviewGenerationResult(
    string Status,
    EvidenceSemanticReviewDocument? Review);
