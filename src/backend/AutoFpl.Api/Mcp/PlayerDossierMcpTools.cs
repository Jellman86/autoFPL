using System.ComponentModel;
using System.Text.RegularExpressions;

using AutoFpl.Api.Sources;
using AutoFpl.Contracts.Sources;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AutoFpl.Api.Mcp;

[McpServerToolType]
public sealed class PlayerDossierMcpTools
{
    private static readonly Regex SeasonCodePattern =
        new(
            @"^\d{4}-\d{2}$",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly OfficialFplPlayerDossierStore _store;

    public PlayerDossierMcpTools(OfficialFplPlayerDossierStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [McpServerTool(
        Name = "get_player_dossier",
        Title = "Get player dossier",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Get one cutoff-correct FPL player dossier with official identity, portrait, "
        + "prior outcomes, upcoming fixtures and admitted pre-deadline research claims. "
        + "Research evidence is quarantined and does not influence Baseline v0.")]
    public async Task<OfficialFplPlayerDossierDocument> GetPlayerDossierAsync(
        [Description("FPL season in YYYY-YY form, for example 2026-27.")]
            string seasonCode,
        [Description("Target FPL Gameweek from 1 to 38.")]
            int gameweek,
        [Description("Official FPL player ID from the target capture.")]
            int playerId,
        CancellationToken cancellationToken)
    {
        if (seasonCode is null
            || seasonCode.Length > 7
            || !SeasonCodePattern.IsMatch(seasonCode))
        {
            throw new McpException(
                "seasonCode must use YYYY-YY form, for example 2026-27.");
        }

        if (gameweek is < 1 or > 38)
        {
            throw new McpException("gameweek must be between 1 and 38.");
        }

        if (playerId < 1)
        {
            throw new McpException("playerId must be a positive official FPL player ID.");
        }

        return await _store.GetAsync(
                seasonCode,
                gameweek,
                playerId,
                cancellationToken)
            ?? throw new McpException(
                "No cutoff-correct player dossier exists for those identifiers.");
    }
}
