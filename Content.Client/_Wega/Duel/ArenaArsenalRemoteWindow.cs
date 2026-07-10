using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Wega.Duel;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Окно пульта арсенала: список тиров крейтов кнопками; текущий помечен и заблокирован.
/// Клик отправляет выбранный тир (или «отключить») на сервер.
/// </summary>
public sealed class ArenaArsenalRemoteWindow : FancyWindow
{
    public event Action<string?>? OnSelect;

    private readonly BoxContainer _list;

    public ArenaArsenalRemoteWindow()
    {
        Title = Loc.GetString("arena-arsenal-remote-title");
        MinSize = new Vector2(360, 320);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        root.AddChild(new Label
        {
            Text = Loc.GetString("arena-arsenal-remote-info"),
            Margin = new Thickness(2, 0, 2, 8),
        });

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };

        _list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        scroll.AddChild(_list);
        root.AddChild(scroll);

        ContentsContainer.AddChild(root);
    }

    public void Populate(ArenaArsenalRemoteBuiState state)
    {
        _list.RemoveAllChildren();

        foreach (var option in state.Options)
        {
            var text = option.Current
                ? Loc.GetString("arena-arsenal-remote-current", ("name", option.Name))
                : option.Name;

            var button = new Button
            {
                Text = text,
                Disabled = option.Current,
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalExpand = true,
            };

            var proto = option.CrateProto;
            button.OnPressed += _ => OnSelect?.Invoke(proto);
            _list.AddChild(button);
        }
    }
}
