using System.IO;
using System.Reflection;
using Content.Server.Chat.Managers;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Update;

/// <summary>
/// Замечает задеплоенное, но ещё не применённое обновление: раз в интервал сравнивает mtime
/// серверной DLL на диске с тем, что было при старте процесса. Пересобрали билд поверх работающего
/// сервера (git pull + dotnet build) — в общий чат уходит анонс «обновление загружено, применится
/// после перезапуска». Повторная пересборка даст новый анонс (базовая метка сдвигается).
/// </summary>
public sealed partial class UpdateDeployedNotifySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private string? _assemblyPath;
    private DateTime _baselineWriteTime;
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        // Location пуст в single-file/собранных иначе сценариях — тогда система просто молчит.
        var path = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        _assemblyPath = path;
        _baselineWriteTime = File.GetLastWriteTimeUtc(path);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_assemblyPath == null || _timing.RealTime < _nextCheck)
            return;

        _nextCheck = _timing.RealTime + CheckInterval;

        DateTime current;
        try
        {
            current = File.GetLastWriteTimeUtc(_assemblyPath);
        }
        catch (IOException)
        {
            // Файл в этот момент перезаписывается сборкой — проверим в следующий раз.
            return;
        }

        if (current <= _baselineWriteTime)
            return;

        _baselineWriteTime = current;
        _chat.DispatchServerAnnouncement(Loc.GetString("update-deployed-announcement"));
    }
}
