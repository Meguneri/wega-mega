using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.MediaPlayer;

/// <summary>Запустить видеоклип на всех ТВ-экранах (прототип кинотеатра).</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class TvPlayCommand : LocalizedEntityCommands
{
    [Dependency] private MediaPlayerSystem _mediaPlayer = default!;

    public override string Command => "tvplay";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Loc.GetString("cmd-tvplay-usage"));
            return;
        }

        _mediaPlayer.TvPlay(string.Join(' ', args), shell.Player);
    }
}

/// <summary>Остановить клип и погасить все ТВ-экраны.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class TvStopCommand : LocalizedEntityCommands
{
    [Dependency] private MediaPlayerSystem _mediaPlayer = default!;

    public override string Command => "tvstop";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _mediaPlayer.TvStop();
    }
}
