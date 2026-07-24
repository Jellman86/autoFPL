using AutoFpl.Domain.Squads;

namespace AutoFpl.Domain.Tests.Squads;

public sealed class SquadTests
{
    [Fact]
    public void CreateReturnsExactBudgetSummaryForValidSquad()
    {
        Squad squad = Squad.Create(budgetTenths: 1_000, ValidPlayers());

        Assert.Equal(15, squad.PlayerCount);
        Assert.Equal(745, squad.TotalCostTenths);
        Assert.Equal(1_000, squad.BudgetTenths);
        Assert.Equal(255, squad.RemainingBudgetTenths);
    }

    [Fact]
    public void CreateRejectsWrongPlayerCount()
    {
        SquadValidationException exception = Assert.Throws<SquadValidationException>(
            () => Squad.Create(1_000, ValidPlayers()[..14]));

        Assert.Equal("squad.players.count", exception.Code);
        Assert.Equal("players", exception.Field);
    }

    [Fact]
    public void CreateRejectsDuplicatePlayerIds()
    {
        SquadPlayer[] players = ValidPlayers();
        players[^1] = SquadPlayer.Create(1, 5, "forward", 60);

        SquadValidationException exception = Assert.Throws<SquadValidationException>(
            () => Squad.Create(1_000, players));

        Assert.Equal("squad.player.duplicate", exception.Code);
        Assert.Equal("players", exception.Field);
    }

    [Fact]
    public void CreateRejectsWrongPositionCounts()
    {
        SquadPlayer[] players = ValidPlayers();
        players[2] = SquadPlayer.Create(3, 1, "midfielder", 45);

        SquadValidationException exception = Assert.Throws<SquadValidationException>(
            () => Squad.Create(1_000, players));

        Assert.Equal("squad.positions.invalid", exception.Code);
        Assert.Equal("players", exception.Field);
    }

    [Fact]
    public void CreateRejectsMoreThanThreePlayersFromOneClub()
    {
        SquadPlayer[] players = ValidPlayers();
        players[3] = SquadPlayer.Create(4, 1, "defender", 45);

        SquadValidationException exception = Assert.Throws<SquadValidationException>(
            () => Squad.Create(1_000, players));

        Assert.Equal("squad.club.limit", exception.Code);
        Assert.Equal("players", exception.Field);
    }

    [Fact]
    public void PlayerRejectsNonPositivePrice()
    {
        SquadValidationException exception = Assert.Throws<SquadValidationException>(
            () => SquadPlayer.Create(1, 1, "goalkeeper", 0));

        Assert.Equal("squad.player.price.invalid", exception.Code);
        Assert.Equal("players[].priceTenths", exception.Field);
    }

    [Fact]
    public void CreateRejectsCostAboveBudget()
    {
        SquadValidationException exception = Assert.Throws<SquadValidationException>(
            () => Squad.Create(700, ValidPlayers()));

        Assert.Equal("squad.budget.exceeded", exception.Code);
        Assert.Equal("budgetTenths", exception.Field);
    }

    [Theory]
    [InlineData(0, 1, "goalkeeper", "squad.player.id.invalid", "players[].playerId")]
    [InlineData(1, 0, "goalkeeper", "squad.player.club.invalid", "players[].clubId")]
    [InlineData(1, 1, "Goalkeeper", "squad.player.position.invalid", "players[].position")]
    public void PlayerRejectsInvalidIdentityOrPosition(
        int playerId,
        int clubId,
        string position,
        string expectedCode,
        string expectedField)
    {
        SquadValidationException exception = Assert.Throws<SquadValidationException>(
            () => SquadPlayer.Create(playerId, clubId, position, 45));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(expectedField, exception.Field);
    }

    private static SquadPlayer[] ValidPlayers() =>
    [
        SquadPlayer.Create(1, 1, "goalkeeper", 45),
        SquadPlayer.Create(2, 2, "goalkeeper", 45),
        SquadPlayer.Create(3, 1, "defender", 45),
        SquadPlayer.Create(4, 2, "defender", 45),
        SquadPlayer.Create(5, 3, "defender", 45),
        SquadPlayer.Create(6, 4, "defender", 45),
        SquadPlayer.Create(7, 5, "defender", 45),
        SquadPlayer.Create(8, 1, "midfielder", 50),
        SquadPlayer.Create(9, 2, "midfielder", 50),
        SquadPlayer.Create(10, 3, "midfielder", 50),
        SquadPlayer.Create(11, 4, "midfielder", 50),
        SquadPlayer.Create(12, 5, "midfielder", 50),
        SquadPlayer.Create(13, 3, "forward", 60),
        SquadPlayer.Create(14, 4, "forward", 60),
        SquadPlayer.Create(15, 5, "forward", 60),
    ];
}
