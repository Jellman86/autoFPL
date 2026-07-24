namespace AutoFpl.Domain.Squads;

public sealed record Squad
{
    private Squad(int budgetTenths, int totalCostTenths)
    {
        BudgetTenths = budgetTenths;
        TotalCostTenths = totalCostTenths;
    }

    public int PlayerCount => 15;

    public int BudgetTenths { get; }

    public int TotalCostTenths { get; }

    public int RemainingBudgetTenths => BudgetTenths - TotalCostTenths;

    public static Squad Create(
        int budgetTenths,
        IReadOnlyCollection<SquadPlayer> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        if (budgetTenths <= 0)
        {
            throw new SquadValidationException(
                "squad.budget.invalid",
                "budgetTenths");
        }
        if (players.Count != 15)
        {
            throw new SquadValidationException(
                "squad.players.count",
                "players");
        }
        if (players.Select(player => player.PlayerId).Distinct().Count() != players.Count)
        {
            throw new SquadValidationException(
                "squad.player.duplicate",
                "players");
        }

        bool positionsAreValid =
            players.Count(player => player.Position == SquadPosition.Goalkeeper) == 2
            && players.Count(player => player.Position == SquadPosition.Defender) == 5
            && players.Count(player => player.Position == SquadPosition.Midfielder) == 5
            && players.Count(player => player.Position == SquadPosition.Forward) == 3;
        if (!positionsAreValid)
        {
            throw new SquadValidationException(
                "squad.positions.invalid",
                "players");
        }
        if (players.GroupBy(player => player.ClubId).Any(group => group.Count() > 3))
        {
            throw new SquadValidationException(
                "squad.club.limit",
                "players");
        }

        long totalCostTenths = players.Sum(player => (long)player.PriceTenths);
        if (totalCostTenths > budgetTenths)
        {
            throw new SquadValidationException(
                "squad.budget.exceeded",
                "budgetTenths");
        }

        return new(budgetTenths, (int)totalCostTenths);
    }
}
