using System.Runtime.CompilerServices;
using LevelRank.Managers;
using Microsoft.Extensions.Logging;
using Sharp.Extensions.GameEventManager;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEvents;
using Sharp.Shared.HookParams;
using Sharp.Shared.Objects;
using Sharp.Shared.Units;

namespace LevelRank.Modules;

internal class DeathModule : IModule
{
    private readonly InterfaceBridge _bridge;

    private readonly IGameEventManager _gameEventManager;
    private readonly IPlayerManager    _playerManager;

    private readonly IScoreModule         _scoreModule;
    private readonly IMessageModule       _messageModule;
    private readonly ILogger<DeathModule> _logger;

    private bool _victimIsHostageCarrier;

    public DeathModule(InterfaceBridge      bridge,
                       IGameEventManager    gameEventManager,
                       IPlayerManager       playerManager,
                       IScoreModule         scoreModule,
                       IMessageModule       messageModule,
                       ILogger<DeathModule> logger)
    {
        _bridge = bridge;

        _gameEventManager = gameEventManager;
        _playerManager    = playerManager;

        _scoreModule   = scoreModule;
        _messageModule = messageModule;

        _logger = logger;
    }

    public bool Init()
    {
        _bridge.HookManager.PlayerKilledPost.InstallForward(OnPlayerKilledPost);
        _bridge.HookManager.PlayerKilledPre.InstallForward(OnPlayerKilledPre);
        _gameEventManager.ListenEvent("player_death", OnGameEventFired);

        return true;
    }

    public void Shutdown()
    {
        _bridge.HookManager.PlayerKilledPost.RemoveForward(OnPlayerKilledPost);
        _bridge.HookManager.PlayerKilledPre.RemoveForward(OnPlayerKilledPre);
    }

    private void OnPlayerKilledPre(IPlayerKilledForwardParams @params)
    {
        _victimIsHostageCarrier = @params.Pawn.GetHostageService() is { } hostageService
                                  && hostageService.CarriedHostageHandle.IsValid();
    }

    private void OnPlayerKilledPost(IPlayerKilledForwardParams @params)
    {
        if (!_scoreModule.IsRankEnabled)
        {
            return;
        }

        // not allowed in Warmup
#if RELEASE
        if (_bridge.GameRules.IsWarmupPeriod)
        {
            return;
        }
#endif

        var client = @params.Client;

        // we also dont count FakeClient
#if RELEASE
        if (client.IsFakeClient)
        {
            return;
        }
#endif
        var attackerSlot = new PlayerSlot((byte) @params.AttackerPlayerSlot);

        var isSuicide = @params.IsWorld || !@params.IsPawn || attackerSlot == client.Slot;

        ProcessVictim(client, isSuicide);

        if (isSuicide)
        {
            return;
        }

        ProcessAttacker(attackerSlot, @params);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessVictim(IGameClient client, bool isSuicide)
    {
        var isMatchEndedSuicide = _bridge.GameRules.GamePhase == GamePhase.MatchEnded && isSuicide;

        if (isMatchEndedSuicide)
        {
            return;
        }

        // Spectator-exclusion: skip players not actively on T or CT at the moment of
        // death processing. Handles the edge case of a player dying during a team-change
        // transition (controller.Team already updated to Spectator before event fires).
        var controllerTeam = client.GetPlayerController()?.Team ?? CStrikeTeam.UnAssigned;

        if (controllerTeam is not (CStrikeTeam.TE or CStrikeTeam.CT))
        {
            return;
        }

        if (_playerManager.GetPlayerRankInfo(client) is not { } victimRank)
        {
            return;
        }

        victimRank.Deaths++;

        var reason     = isSuicide ? ScoreAction.Suicides : ScoreAction.Deaths;
        var scoreDelta = _scoreModule.GetScoreForAction(reason);
        _playerManager.UpdateScore(client, scoreDelta);

        _messageModule.SendScoreUpdate(client, victimRank.Score, [reason]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessAttacker(PlayerSlot attackerSlot, IPlayerKilledForwardParams @params)
    {
        if (attackerSlot < 0
            || _bridge.ClientManager.GetGameClient(attackerSlot) is not { } attacker
            || _playerManager.GetPlayerRankInfo(attackerSlot) is not { } attackerRank)
        {
            return;
        }

        // Spectator-exclusion: skip kill credit if attacker is no longer on T or CT
        // (e.g., killed while team-change transition was in flight).
        var attackerTeam = attacker.GetPlayerController()?.Team ?? CStrikeTeam.UnAssigned;

        if (attackerTeam is not (CStrikeTeam.TE or CStrikeTeam.CT))
        {
            return;
        }

        var inflictor = _bridge.EntityManager.FindEntityByHandle(@params.InflictorHandle);

        if (inflictor is null)
        {
            return;
        }

        Span<ScoreAction> actions     = stackalloc ScoreAction[5];
        var               actionCount = 0;

        attackerRank.Kills++;
        actions[actionCount++] = ScoreAction.Kills;

        if (_victimIsHostageCarrier)
        {
            attackerRank.PreventHostageRescues++;
            actions[actionCount++] = ScoreAction.PreventHostageRescue;
        }

        var inflictorClassname = inflictor.Classname;

        if (IsUtilityDamage(inflictorClassname))
        {
            attackerRank.UtilitiesKills++;
            actions[actionCount++] = ScoreAction.UtilitiesKills;
        }
        else
        {
            var attackEntity = _bridge.EntityManager.FindEntityByHandle(@params.AbilityHandle);

            if (attackEntity?.AsBaseWeapon() is not { } weapon)
            {
                return;
            }

            if (weapon.ItemDefinitionIndex == (ushort) EconItemId.Taser)
            {
                attackerRank.ZeusKills++;
                actions[actionCount++] = ScoreAction.ZeusKills;
            }
            else if (weapon.IsKnife)
            {
                attackerRank.KnifeKills++;
                actions[actionCount++] = ScoreAction.KnifeKills;
            }
        }

        var isHeadShot = (@params.DamageType & DamageFlagBits.Headshot) != 0 || @params.HitGroup == HitGroupType.Head;

        if (isHeadShot)
        {
            attackerRank.Headshots++;
            actions[actionCount++] = ScoreAction.Headshots;
        }

        var actionsSlice    = actions[..actionCount];
        var totalScoreDelta = 0;

        foreach (var action in actionsSlice)
        {
            totalScoreDelta += _scoreModule.GetScoreForAction(action);
        }

        _playerManager.UpdateScore(attacker, totalScoreDelta);
        _messageModule.SendScoreUpdate(attacker, attackerRank.Score, actionsSlice);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUtilityDamage(string classname)
        => classname is "inferno" or
            "hegrenade_projectile" or
            "decoy_projectile" or
            "flashbang_projectile" or
            "smokegrenade_projectile" or
            "molotov_projectile";

    private void OnGameEventFired(IGameEvent e)
    {
        if (!_scoreModule.IsRankEnabled)
        {
            return;
        }

        // not allowed in Warmup
#if RELEASE
        if (_bridge.GameRules.IsWarmupPeriod)
        {
            return;
        }
#endif

        if (e is not IEventPlayerDeath ev)
        {
            return;
        }

        if (ev.VictimController is not { } victim || victim.IsFakeClient)
        {
            return;
        }

        if (ev.AssisterController is not { } assister)
        {
            return;
        }

        // Spectator-exclusion: assists from spectating players are not credited.
        if (assister.Team is not (CStrikeTeam.TE or CStrikeTeam.CT))
        {
            return;
        }

        if (_playerManager.GetPlayerRankInfo(assister.PlayerSlot) is not { } rank)
        {
            return;
        }

        rank.Assists++;
        var score = _scoreModule.GetScoreForAction(ScoreAction.Assists);
        _playerManager.UpdateScore(assister.PlayerSlot, score);

        _messageModule.SendScoreUpdate(_bridge.ClientManager.GetGameClient(assister.PlayerSlot)!,
                                       rank.Score,
                                       [ScoreAction.Assists]);
    }
}
