using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Client.MediaPlayer;

[AnyCommand]
public sealed class MediaPlayerCommand : LocalizedCommands
{
    public override string Command => "mediaplayer";

    public override string Description => LocalizationManager.GetString("cmd-mediaplayer-desc");

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        IoCManager.Resolve<IEntitySystemManager>()
            .GetEntitySystem<MediaPlayerSystem>()
            .OpenWindow();
    }
}
