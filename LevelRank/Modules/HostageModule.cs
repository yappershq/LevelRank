using LevelRank.Managers;
using Microsoft.Extensions.Logging;
using Sharp.Extensions.GameEventManager;
using Sharp.Shared.Enums;
using Sharp.Shared.Objects;

namespace LevelRank.Modules;

internal class HostageModule : IModule
{
    private readonly InterfaceBridge _bridge;

    private readonly IGameEventManager _gameEventManager;
    private readonly IPlayerManager    _playerManager;

    private readonly IScoreModule   _scoreModule;
    private readonly IMessageModule _messageModule;

    private readonly ILogger<HostageModule> _logger;

    public HostageModule(InterfaceBridge        bridge,
                         IGameEventManager      gameEventManager,
                         IPlayerManager         playerManager,
                         IScoreModule           scoreModule,
                         IMessageModule         messageModule,
                         ILogger<HostageModule> logger)
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
        _gameEventManager.ListenEvent("hostage_rescued", OnHostageRescured);

        return true;
    }

    private void OnHostageRescured(IGameEvent e)
    {
        if (!_scoreModule.IsRankEnabled)
        {
            return;
        }

        if (e.GetPlayerController("userid") is not { } controller)
        {
            return;
        }

        if (controller.IsFakeClient)
        {
            return;
        }

        // Defensive: hostage_rescued can only fire for an active CT, but guard
        // explicitly so any future regression has a clear team-exclusion barrier.
        if (controller.Team != CStrikeTeam.CT)
        {
            return;
        }

        if (_playerManager.GetPlayerRankInfo(controller.PlayerSlot) is not { } rank)
        {
            return;
        }

        rank.HostageRescues++;
        var score = _scoreModule.GetScoreForAction(ScoreAction.HostageRescues);

        _playerManager.UpdateScore(controller.PlayerSlot, score);

        _messageModule.SendScoreUpdate(_bridge.ClientManager.GetGameClient(controller.PlayerSlot)!,
                                       rank.Score,
                                       [ScoreAction.HostageRescues]);
    }
}