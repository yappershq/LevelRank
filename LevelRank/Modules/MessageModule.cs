using Cysharp.Text;
using LevelRank.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sharp.Shared.Enums;
using Sharp.Shared.Objects;

namespace LevelRank.Modules;

internal class ColorConfig
{
    public string Prefix     { get; set; } = "{Lime}";
    public string ScoreLabel { get; set; } = "{White}";
    public string ScoreValue { get; set; } = "{Gold}";
    public string ScoreGain  { get; set; } = "{LightGreen}";
    public string ScoreLoss  { get; set; } = "{LightRed}";
    public string ActionText { get; set; } = "{Silver}";
    public string Separator  { get; set; } = "{Grey}";
    public string Brackets   { get; set; } = "{Grey}";
}

internal interface IMessageModule
{
    void SendScoreUpdate(IGameClient client, ulong newTotalScore, ReadOnlySpan<ScoreAction> actions);
}

internal class MessageModule : IModule, IMessageModule
{
    private readonly InterfaceBridge        _bridge;
    private readonly ILogger<MessageModule> _logger;
    private readonly IScoreModule           _scoreModule;
    private readonly ColorConfig            _colors;

    public MessageModule(InterfaceBridge bridge, IScoreModule scoreModule, IConfiguration configuration, ILogger<MessageModule> logger)
    {
        _bridge      = bridge;
        _logger      = logger;
        _scoreModule = scoreModule;

        _colors = new ();
        configuration.GetSection("Colors").Bind(_colors);

        _colors.Prefix     = _colors.Prefix.RemoveColorPlaceholder();
        _colors.ScoreLabel = _colors.ScoreLabel.RemoveColorPlaceholder();
        _colors.ScoreValue = _colors.ScoreValue.RemoveColorPlaceholder();
        _colors.ScoreGain  = _colors.ScoreGain.RemoveColorPlaceholder();
        _colors.ScoreLoss  = _colors.ScoreLoss.RemoveColorPlaceholder();
        _colors.ActionText = _colors.ActionText.RemoveColorPlaceholder();
        _colors.Separator  = _colors.Separator.RemoveColorPlaceholder();
        _colors.Brackets   = _colors.Brackets.RemoveColorPlaceholder();
    }

    public bool Init()
    {
        _logger.LogInformation("MessageModule initialized");

        return true;
    }

    public void Shutdown()
    {
    }

    private const string ActionKeyPrefix = "LevelRank.";

    public void SendScoreUpdate(IGameClient client, ulong newTotalScore, ReadOnlySpan<ScoreAction> actions)
    {
        if (client.IsFakeClient || client.IsHltv)
        {
            return;
        }

        if (client.GetPlayerController() is not { } controller)
        {
            return;
        }

        var localizeManager = _bridge.GetLocalizerManager();
        var localizer       = localizeManager.For(client);

        using var sb = ZString.CreateStringBuilder();
        using var actionKeySb = ZString.CreateStringBuilder();

        sb.Append(' ');
        sb.Append(_colors.Prefix);
        sb.Append("[LevelRank] ");
        sb.Append(_colors.ScoreLabel);
        sb.Append(localizer.Text("LevelRank.TotalScore"));
        sb.Append(": ");
        sb.Append(_colors.ScoreValue);
        sb.Append(newTotalScore);
        sb.Append(' ');
        sb.Append(_colors.Brackets);
        sb.Append('(');

        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            var score  = _scoreModule.GetScoreForAction(action);

            actionKeySb.Clear();
            actionKeySb.Append(ActionKeyPrefix);
            actionKeySb.Append(action);
            var reasonText = localizer.Text(actionKeySb.ToString());

            sb.Append(score >= 0 ? _colors.ScoreGain : _colors.ScoreLoss);
            if (score >= 0)
            {
                sb.Append('+');
            }
            sb.Append(score);
            sb.Append(' ');
            sb.Append(_colors.ActionText);
            sb.Append(reasonText);

            if (i < actions.Length - 1)
            {
                sb.Append(' ');
                sb.Append(_colors.Separator);
                sb.Append("| ");
            }
        }

        sb.Append(_colors.Brackets);
        sb.Append(')');

        controller.Print(HudPrintChannel.SayText2, sb.ToString());
    }
}