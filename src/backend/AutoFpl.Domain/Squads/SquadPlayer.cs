namespace AutoFpl.Domain.Squads;

public enum SquadPosition
{
    Goalkeeper,
    Defender,
    Midfielder,
    Forward,
}

public sealed record SquadPlayer
{
    private SquadPlayer(
        int playerId,
        int clubId,
        SquadPosition position,
        int priceTenths)
    {
        PlayerId = playerId;
        ClubId = clubId;
        Position = position;
        PriceTenths = priceTenths;
    }

    public int PlayerId { get; }

    public int ClubId { get; }

    public SquadPosition Position { get; }

    public int PriceTenths { get; }

    public static SquadPlayer Create(
        int playerId,
        int clubId,
        string position,
        int priceTenths)
    {
        if (playerId <= 0)
        {
            throw new SquadValidationException(
                "squad.player.id.invalid",
                "players[].playerId");
        }
        if (clubId <= 0)
        {
            throw new SquadValidationException(
                "squad.player.club.invalid",
                "players[].clubId");
        }
        if (priceTenths <= 0)
        {
            throw new SquadValidationException(
                "squad.player.price.invalid",
                "players[].priceTenths");
        }

        SquadPosition parsedPosition = position switch
        {
            "goalkeeper" => SquadPosition.Goalkeeper,
            "defender" => SquadPosition.Defender,
            "midfielder" => SquadPosition.Midfielder,
            "forward" => SquadPosition.Forward,
            _ => throw new SquadValidationException(
                "squad.player.position.invalid",
                "players[].position"),
        };

        return new(playerId, clubId, parsedPosition, priceTenths);
    }
}
