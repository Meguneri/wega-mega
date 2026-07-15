using System.Numerics;
using Content.Client._Wega.MediaPlayer.UI;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.MediaPlayer;

/// <summary>
/// Drives the small HUD indicator that appears while the global media player is playing.
/// </summary>
public sealed class MediaPlayerIndicatorUIController : UIController
{
    [UISystemDependency] private readonly MediaPlayerSystem _mediaPlayer = default!;

    /// <summary>
    /// Distance from the top of the screen, so the indicator sits below the top menu bar.
    /// </summary>
    private const float TopMargin = 70f;

    public override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var screen = UIManager.ActiveScreen;
        if (screen == null)
            return;

        var indicator = screen.GetOrAddWidget<MediaPlayerIndicator>();
        // Wire the click-to-open once; GetOrAddWidget returns the same instance each frame.
        indicator.ClickAction ??= _mediaPlayer.OpenWindow;

        var state = _mediaPlayer.LastState;
        var loaded = state is { TrackId: not null };

        indicator.Visible = loaded;
        if (!loaded)
            return;

        var title = MediaPlayerSystem.SanitizeTitle(state!.Title);
        if (!state.Playing)
            title = Loc.GetString("ui-media-player-indicator-paused", ("title", title));
        indicator.SetTrack(title);

        // Keep it centered horizontally, anchored just under the top bar.
        var x = (screen.Size.X - indicator.DesiredSize.X) / 2f;
        LayoutContainer.SetPosition(indicator, new Vector2(MathF.Max(x, 0f), TopMargin));
    }
}
