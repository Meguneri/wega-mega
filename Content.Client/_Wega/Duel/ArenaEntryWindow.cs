using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Wega.Duel;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Окно кнопки входа на арену: список тиров арсенал-ящиков (одиночный выбор, выделяется нажатием) и
/// кнопка «Войти». Пока тир не выбран — «Войти» заблокирована. Нажатие «Войти» отправляет выбранный
/// тир на сервер: он применяется ко всем аренам, дуэлянтов телепортирует, ящики спавнятся у спавнов.
/// </summary>
public sealed class ArenaEntryWindow : FancyWindow
{
    public event Action<string?>? OnConfirm;

    private readonly BoxContainer _list;
    private readonly Button _enter;
    private readonly List<(string? Proto, Button Button)> _buttons = new();
    private string? _selected;
    private bool _hasSelection;

    public ArenaEntryWindow()
    {
        Title = Loc.GetString("arena-entry-title");
        MinSize = new Vector2(360, 340);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        root.AddChild(new Label
        {
            Text = Loc.GetString("arena-entry-info"),
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

        _enter = new Button
        {
            Text = Loc.GetString("arena-entry-enter"),
            Disabled = true,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalExpand = true,
        };
        _enter.OnPressed += _ =>
        {
            if (_hasSelection)
                OnConfirm?.Invoke(_selected);
        };
        root.AddChild(_enter);

        ContentsContainer.AddChild(root);
    }

    public void Populate(ArenaArsenalRemoteBuiState state)
    {
        _list.RemoveAllChildren();
        _buttons.Clear();
        _hasSelection = false;
        _selected = null;
        _enter.Disabled = true;

        foreach (var option in state.Options)
        {
            var button = new Button
            {
                Text = option.Name,
                ToggleMode = true,
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalExpand = true,
            };

            var proto = option.CrateProto;

            // Пред-выделяем текущий тир арены (если задан) — чтобы было видно, что сейчас активно.
            if (option.Current)
            {
                button.Pressed = true;
                _selected = proto;
                _hasSelection = true;
                _enter.Disabled = false;
            }

            button.OnPressed += _ => Select(proto);
            _buttons.Add((proto, button));
            _list.AddChild(button);
        }
    }

    // Радио-поведение: выделяем один тир, снимаем выделение с остальных, разблокируем «Войти».
    private void Select(string? proto)
    {
        _selected = proto;
        _hasSelection = true;
        _enter.Disabled = false;

        foreach (var (p, b) in _buttons)
            b.Pressed = Equals(p, proto);
    }
}
