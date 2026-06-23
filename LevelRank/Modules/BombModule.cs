using LevelRank.Managers;
using Microsoft.Extensions.Logging;
using Sharp.Extensions.GameEventManager;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEvents;
using Sharp.Shared.Objects;

namespace LevelRank.Modules;

internal class BombModule : IModule
{
    private readonly InterfaceBridge _bridge;

    private readonly IGameEventManager _gameEventManager;
    private readonly IPlayerManager    _playerManager;

    private readonly IScoreModule   _scoreModule;
    private readonly IMessageModule _messageModule;

    private readonly ILogger<BombModule> _logger;

    public BombModule(InterfaceBridge     bridge,
                      IGameEventManager   gameEventManager,
                      IPlayerManager      playerManager,
                      IScoreModule        scoreModule,
                      IMessageModule      messageModule,
                      ILogger<BombModule> logger)
    {
        _bridge           = bridge;
        _gameEventManager = gameEventManager;
        _playerManager    = playerManager;
        _scoreModule      = scoreModule;
        _messageModule    = messageModule;
        _logger           = logger;
    }

    public bool Init()
    {
        _gameEventManager.ListenEvent("bomb_planted", OnBombPlanted);
        _gameEventManager.ListenEvent("bomb_defused", OnBombDefused);

        return true;
    }

    private void OnBombPlanted(IGameEvent e)
    {
        if (!_scoreModule.IsRankEnabled)
        {
            return;
        }

        if (e is not IEventBombPlanted ev)
        {
            return;
        }

        if (ev.Controller is not { } controller)
        {
            return;
        }

        // Defensive: bomb_planted can only fire for an active T, but guard explicitly
        // so any future regression has a clear team-exclusion barrier.
        if (controller.Team != CStrikeTeam.TE)
        {
            return;
        }

        if (_playerManager.GetPlayerRankInfo(controller.PlayerSlot) is not { } rankInfo)
        {
            return;
        }

        rankInfo.BombPlants++;
        var score = _scoreModule.GetScoreForAction(ScoreAction.BombPlants);
        _playerManager.UpdateScore(controller.PlayerSlot, score);

        _messageModule.SendScoreUpdate(_bridge.ClientManager.GetGameClient(controller.PlayerSlot)!,
                                       rankInfo.Score,
                                       [ScoreAction.BombPlants]);
    }

    private void OnBombDefused(IGameEvent e)
    {
        if (!_scoreModule.IsRankEnabled)
        {
            return;
        }

        if (e is not IEventBombDefused ev)
        {
            return;
        }

        if (ev.Controller is not { } controller)
        {
            return;
        }

        // Defensive: bomb_defused can only fire for an active CT, but guard explicitly
        // so any future regression has a clear team-exclusion barrier.
        if (controller.Team != CStrikeTeam.CT)
        {
            return;
        }

        if (_playerManager.GetPlayerRankInfo(controller.PlayerSlot) is not { } rankInfo)
        {
            return;
        }

        rankInfo.BombDefuses++;

        var score = _scoreModule.GetScoreForAction(ScoreAction.BombDefuses);
        _playerManager.UpdateScore(controller.PlayerSlot, score);

        _messageModule.SendScoreUpdate(_bridge.ClientManager.GetGameClient(controller.PlayerSlot)!,
                                       rankInfo.Score,
                                       [ScoreAction.BombDefuses]);
    }
}
