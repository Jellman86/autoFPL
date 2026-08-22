using AutoFpl.Api.Sources;

using Xunit;

namespace AutoFpl.Api.Tests;

/// <summary>
/// Covers the bounded gameweek selection that decides which final outcomes a poll
/// imports. The importer itself is covered by
/// <see cref="OfficialFplOutcomeImporterTests"/>; this fixes the selection rule that
/// decides whether it is called at all.
/// </summary>
public sealed class OfficialFplOutcomeCandidateSelectionTests
{
    [Fact]
    public void No_completed_gameweek_selects_nothing()
    {
        IReadOnlyList<int> candidates = OfficialFplPoller.SelectOutcomeCandidates(
            latestCompletedGameweek: null,
            capturedGameweeks: new HashSet<int>(),
            maximumImports: OfficialFplPoller.MaximumOutcomeImportsPerPoll);

        Assert.Empty(candidates);
    }

    [Fact]
    public void First_completed_gameweek_is_selected_once_it_completes()
    {
        IReadOnlyList<int> candidates = OfficialFplPoller.SelectOutcomeCandidates(
            latestCompletedGameweek: 1,
            capturedGameweeks: new HashSet<int>(),
            maximumImports: OfficialFplPoller.MaximumOutcomeImportsPerPoll);

        Assert.Equal([1], candidates);
    }

    [Fact]
    public void Latest_completed_gameweek_is_reselected_after_capture()
    {
        // The official API revises bonus points and underlying stats after a
        // gameweek first completes, so the most recent gameweek stays selected.
        // Re-import is safe because a final outcome is immutable and idempotent,
        // and corrected content is stored as a new immutable outcome.
        IReadOnlyList<int> candidates = OfficialFplPoller.SelectOutcomeCandidates(
            latestCompletedGameweek: 1,
            capturedGameweeks: new HashSet<int> { 1 },
            maximumImports: OfficialFplPoller.MaximumOutcomeImportsPerPoll);

        Assert.Equal([1], candidates);
    }

    [Fact]
    public void Fully_captured_season_reselects_only_the_latest_gameweek()
    {
        IReadOnlyList<int> candidates = OfficialFplPoller.SelectOutcomeCandidates(
            latestCompletedGameweek: 5,
            capturedGameweeks: new HashSet<int> { 1, 2, 3, 4, 5 },
            maximumImports: OfficialFplPoller.MaximumOutcomeImportsPerPoll);

        Assert.Equal([5], candidates);
    }

    [Fact]
    public void Backfill_is_bounded_by_the_import_limit()
    {
        IReadOnlyList<int> candidates = OfficialFplPoller.SelectOutcomeCandidates(
            latestCompletedGameweek: 10,
            capturedGameweeks: new HashSet<int>(),
            maximumImports: OfficialFplPoller.MaximumOutcomeImportsPerPoll);

        Assert.Equal([1, 2, 3], candidates);
        Assert.True(candidates.Count <= OfficialFplPoller.MaximumOutcomeImportsPerPoll);
    }

    [Fact]
    public void Backfill_skips_captured_gameweeks_and_keeps_the_latest()
    {
        IReadOnlyList<int> candidates = OfficialFplPoller.SelectOutcomeCandidates(
            latestCompletedGameweek: 5,
            capturedGameweeks: new HashSet<int> { 1, 2, 4 },
            maximumImports: OfficialFplPoller.MaximumOutcomeImportsPerPoll);

        Assert.Equal([3, 5], candidates);
    }

    [Fact]
    public void Selection_never_exceeds_the_latest_completed_gameweek()
    {
        IReadOnlyList<int> candidates = OfficialFplPoller.SelectOutcomeCandidates(
            latestCompletedGameweek: 2,
            capturedGameweeks: new HashSet<int>(),
            maximumImports: OfficialFplPoller.MaximumOutcomeImportsPerPoll);

        Assert.All(candidates, gameweek => Assert.InRange(gameweek, 1, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(39)]
    public void Out_of_season_gameweek_is_rejected(int latestCompletedGameweek)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfficialFplPoller.SelectOutcomeCandidates(
                latestCompletedGameweek,
                new HashSet<int>(),
                OfficialFplPoller.MaximumOutcomeImportsPerPoll));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(39)]
    public void Out_of_range_import_limit_is_rejected(int maximumImports)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfficialFplPoller.SelectOutcomeCandidates(
                latestCompletedGameweek: 1,
                capturedGameweeks: new HashSet<int>(),
                maximumImports: maximumImports));
    }

    [Fact]
    public void Missing_captured_set_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => OfficialFplPoller.SelectOutcomeCandidates(
                latestCompletedGameweek: 1,
                capturedGameweeks: null!,
                maximumImports: OfficialFplPoller.MaximumOutcomeImportsPerPoll));
    }
}
