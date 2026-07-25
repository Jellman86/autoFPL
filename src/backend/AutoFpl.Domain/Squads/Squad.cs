namespace AutoFpl.Domain.Squads;

public sealed record Squad
{
    private Squad(
        int budgetTenths,
        int totalCostTenths,
        IReadOnlyList<SquadPlayer> players)
    {
        BudgetTenths = budgetTenths;
        TotalCostTenths = totalCostTenths;
        Players = players;
    }

    public int PlayerCount => 15;

    public int BudgetTenths { get; }

    public int TotalCostTenths { get; }

    public int RemainingBudgetTenths => BudgetTenths - TotalCostTenths;

    public IReadOnlyList<SquadPlayer> Players { get; }

    public static Squad Create(
        int budgetTenths,
        IReadOnlyCollection<SquadPlayer> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        SquadPlayer[] playerSnapshot = [.. players];

        if (budgetTenths <= 0)
        {
            throw new SquadValidationException(
                "squad.budget.invalid",
                "budgetTenths");
        }
        if (playerSnapshot.Length != 15)
        {
            throw new SquadValidationException(
                "squad.players.count",
                "players");
        }
        if (playerSnapshot.Select(player => player.PlayerId).Distinct().Count() != playerSnapshot.Length)
        {
            throw new SquadValidationException(
                "squad.player.duplicate",
                "players");
        }

        bool positionsAreValid =
            playerSnapshot.Count(player => player.Position == SquadPosition.Goalkeeper) == 2
            && playerSnapshot.Count(player => player.Position == SquadPosition.Defender) == 5
            && playerSnapshot.Count(player => player.Position == SquadPosition.Midfielder) == 5
            && playerSnapshot.Count(player => player.Position == SquadPosition.Forward) == 3;
        if (!positionsAreValid)
        {
            throw new SquadValidationException(
                "squad.positions.invalid",
                "players");
        }
        if (playerSnapshot.GroupBy(player => player.ClubId).Any(group => group.Count() > 3))
        {
            throw new SquadValidationException(
                "squad.club.limit",
                "players");
        }

        long totalCostTenths = playerSnapshot.Sum(player => (long)player.PriceTenths);
        if (totalCostTenths > budgetTenths)
        {
            throw new SquadValidationException(
                "squad.budget.exceeded",
                "budgetTenths");
        }

        return new(
            budgetTenths,
            (int)totalCostTenths,
            Array.AsReadOnly(playerSnapshot));
    }
}
