namespace AutoFpl.Domain.Outcomes;

public sealed record EffectivePlayerScore(
    int PlayerId,
    int Points,
    int Multiplier,
    long CountedPoints);
