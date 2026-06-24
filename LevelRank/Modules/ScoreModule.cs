using LevelRank.Managers;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.Objects;

namespace LevelRank.Modules;

internal interface IScoreModule
{
    bool IsRankEnabled { get; }

    int DefaultScore { get; }

    int GetScoreForAction(ScoreAction action);
}

internal class ScoreModule : IModule, IScoreModule
{
    private readonly InterfaceBridge      _bridge;
    private readonly ILogger<ScoreModule> _logger;

    private readonly IConVar _killReward;
    private readonly IConVar _deathPenalty;
    private readonly IConVar _suicidePenalty;
    private readonly IConVar _headshotBonus;
    private readonly IConVar _knifeKillBonus;
    private readonly IConVar _zeusKillBonus;
    private readonly IConVar _utilitiesKillBonus;
    private readonly IConVar _assistReward;
    private readonly IConVar _bombPlantReward;
    private readonly IConVar _bombDefuseReward;
    private readonly IConVar _roundWinReward;
    private readonly IConVar _roundLossPenalty;
    private readonly IConVar _hostageRescueReward;
    private readonly IConVar _hostagePreventRescueReward;
    private readonly IConVar _minPlayers;
    private readonly IConVar _defaultScore;

    public ScoreModule(InterfaceBridge      bridge,
                       IConfigManager       configManager,
                       ILogger<ScoreModule> logger)
    {
        _bridge = bridge;
        _logger = logger;

        _minPlayers   = configManager.CreateConVar("lr_min_players",    5,    "Minimum active (T/CT) players required to enable rank");
        _defaultScore = configManager.CreateConVar("lr_default_score", 1100, "Default score for new players");

        _killReward          = configManager.CreateConVar("lr_score_kill",           3,  "Score reward for a kill");
        _deathPenalty        = configManager.CreateConVar("lr_score_death",          -2, "Score penalty for dying");
        _suicidePenalty      = configManager.CreateConVar("lr_score_suicide",        -3, "Score penalty for suiciding");
        _headshotBonus       = configManager.CreateConVar("lr_score_headshot",       1,  "Bonus score for headshot kills");
        _knifeKillBonus      = configManager.CreateConVar("lr_score_knife",          2,  "Bonus score for knife kills");
        _zeusKillBonus       = configManager.CreateConVar("lr_score_zeus",           2,  "Bonus score for zeus kills");
        _utilitiesKillBonus  = configManager.CreateConVar("lr_score_utilities",      2,  "Bonus score for utilities kills");
        _assistReward        = configManager.CreateConVar("lr_score_assist",         1,  "Score reward for assists");
        _bombPlantReward     = configManager.CreateConVar("lr_score_bomb_plant",     1,  "Score reward for planting the bomb");
        _bombDefuseReward    = configManager.CreateConVar("lr_score_bomb_defuse",    1,  "Score reward for defusing the bomb");
        _roundWinReward      = configManager.CreateConVar("lr_score_round_win",      2,  "Score reward for winning a round");
        _roundLossPenalty    = configManager.CreateConVar("lr_score_round_loss",     -1, "Score penalty for losing a round");
        _hostageRescueReward = configManager.CreateConVar("lr_score_hostage_rescue", 1,  "Score reward for rescuing a hostage");

        _hostagePreventRescueReward
            = configManager.CreateConVar("lr_score_hostage_prevent_rescue",
                                         1,
                                         "Score reward for preventing a hostage from rescuing");
    }

    public bool Init()
    {
        return true;
    }

    public int DefaultScore => _defaultScore.GetInt32();

    public bool IsRankEnabled
    {
        get
        {
            if (_bridge.GameRules.IsWarmupPeriod)
            {
                return false;
            }

            var minPlayers = _minPlayers.GetInt32();

            if (minPlayers <= 0)
            {
                return true;
            }

            // Anti-abuse: count only humans actively on T or CT.
            // Spectators, observers, and HLTV/bots are excluded so that
            // spectating alts cannot pad the threshold to enable point farming.
            var activePlayers = 0;

            foreach (var client in _bridge.ClientManager.GetGameClients(true))
            {
                if (client.IsFakeClient || client.IsHltv)
                {
                    continue;
                }

                var team = client.GetPlayerController()?.Team ?? CStrikeTeam.UnAssigned;

                if (team == CStrikeTeam.TE || team == CStrikeTeam.CT)
                {
                    activePlayers++;
                }
            }

            return activePlayers >= minPlayers;
        }
    }

    public int GetScoreForAction(ScoreAction action)
        => action switch
        {
            ScoreAction.Kills                => _killReward.GetInt32(),
            ScoreAction.Deaths               => _deathPenalty.GetInt32(),
            ScoreAction.Suicides             => _suicidePenalty.GetInt32(),
            ScoreAction.Assists              => _assistReward.GetInt32(),
            ScoreAction.Headshots            => _headshotBonus.GetInt32(),
            ScoreAction.KnifeKills           => _knifeKillBonus.GetInt32(),
            ScoreAction.ZeusKills            => _zeusKillBonus.GetInt32(),
            ScoreAction.UtilitiesKills       => _utilitiesKillBonus.GetInt32(),
            ScoreAction.PreventHostageRescue => _hostagePreventRescueReward.GetInt32(),
            ScoreAction.BombPlants           => _bombPlantReward.GetInt32(),
            ScoreAction.BombDefuses          => _bombDefuseReward.GetInt32(),
            ScoreAction.HostageRescues       => _hostageRescueReward.GetInt32(),
            ScoreAction.RoundWins            => _roundWinReward.GetInt32(),
            ScoreAction.RoundLosses          => _roundLossPenalty.GetInt32(),
            _                                => throw new NotSupportedException($"Action '{action}' not supported"),
        };
}
