using Robust.Shared.GameObjects;

namespace Content.Server._Wega.Chat;

/// <summary>
/// Wega: поднимается броадкастом на каждый свободный (текстовый) эмоут — движок, в отличие от речи
/// (EntitySpokeEvent), для эмоутов события не даёт. Нужен LLM-NPC, чтобы «видеть» действия вокруг.
/// </summary>
public sealed class EntityEmotedEvent : EntityEventArgs
{
    public readonly EntityUid Source;
    public readonly string Action;

    public EntityEmotedEvent(EntityUid source, string action)
    {
        Source = source;
        Action = action;
    }
}

/// <summary>
/// Wega: поднимается броадкастом на каждое станционное объявление — LLM-NPC слышит их и может
/// обсуждать события станции (шаттл, угрозы, смена кода) с гостями.
/// </summary>
public sealed class StationAnnouncedEvent : EntityEventArgs
{
    public readonly string Sender;
    public readonly string Message;

    public StationAnnouncedEvent(string sender, string message)
    {
        Sender = sender;
        Message = message;
    }
}
