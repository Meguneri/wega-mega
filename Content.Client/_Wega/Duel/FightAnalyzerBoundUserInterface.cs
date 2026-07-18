using Content.Shared._Wega.Duel;
using Robust.Client.UserInterface;

namespace Content.Client._Wega.Duel;

public sealed class FightAnalyzerBoundUserInterface : BoundUserInterface
{
    private FightAnalyzerWindow? _window;

    public FightAnalyzerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<FightAnalyzerWindow>();
        _window.OnPrint += number => SendMessage(new FightAnalyzerPrintMessage(number));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is FightAnalyzerBuiState cast)
            _window?.Populate(cast);
    }
}
