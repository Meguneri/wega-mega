using System.IO;
using System.Linq;
using Content.Server.Chat.Managers;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Update;

/// <summary>
/// Announces a pending Git update without installing it or restarting the round.
/// </summary>
public sealed partial class UpdateDeployedNotifySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _nextCheck;
    private string? _announcedCommit;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.RealTime < _nextCheck)
            return;

        _nextCheck = _timing.RealTime + CheckInterval;
        var path = _cfg.GetCVar(WegaCVars.UpdateNoticeFile);
        if (string.IsNullOrWhiteSpace(path) || _players.PlayerCount == 0)
            return;

        string commit;
        try
        {
            commit = File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (commit.Length != 40 || !commit.All(Uri.IsHexDigit) || commit == _announcedCommit)
            return;

        _announcedCommit = commit;
        _chat.DispatchServerAnnouncement(Loc.GetString("update-available-announcement"));
    }
}
