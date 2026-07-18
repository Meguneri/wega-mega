using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Wega.Duel;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Окно боевого анализатора: слева список («Сводка сессии» + дуэли по номерам), справа —
/// подробный отчёт с графиками (та же разметка, что на бумажной распечатке), внизу — печать.
/// </summary>
public sealed class FightAnalyzerWindow : FancyWindow
{
    private static readonly Color ListBackground = Color.FromHex("#1f2027");
    private static readonly Color ListBorder = Color.FromHex("#3a3d4a");
    private static readonly Color AccentGreen = Color.FromHex("#3fbf5a");

    /// <summary>Печать выбранного отчёта: номер дуэли, 0 = сводка сессии.</summary>
    public event Action<int>? OnPrint;

    private readonly BoxContainer _list;
    private readonly RichTextLabel _report;
    private readonly Button _printButton;

    private FightAnalyzerBuiState _state = new();
    private int _selected; // 0 = сводка сессии

    public FightAnalyzerWindow()
    {
        Title = Loc.GetString("fight-analyzer-window-title");
        MinSize = new Vector2(720, 520);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var columns = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        // Левая колонка: список записей.
        var listScroll = new ScrollContainer
        {
            MinWidth = 250,
            VerticalExpand = true,
            HScrollEnabled = false,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        listScroll.AddChild(_list);
        columns.AddChild(listScroll);

        // Правая колонка: отчёт.
        var reportPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = ListBackground,
                BorderColor = ListBorder,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 12,
                ContentMarginRightOverride = 12,
                ContentMarginTopOverride = 8,
                ContentMarginBottomOverride = 8,
            },
        };
        var reportScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        _report = new RichTextLabel { HorizontalExpand = true };
        reportScroll.AddChild(_report);
        reportPanel.AddChild(reportScroll);
        columns.AddChild(reportPanel);

        root.AddChild(columns);

        // Низ: печать.
        _printButton = new Button
        {
            Text = Loc.GetString("fight-analyzer-print"),
            HorizontalAlignment = Control.HAlignment.Right,
            MinWidth = 160,
            Margin = new Thickness(0, 8, 0, 0),
        };
        _printButton.OnPressed += _ => OnPrint?.Invoke(_selected);
        root.AddChild(_printButton);

        ContentsContainer.AddChild(root);
    }

    public void Populate(FightAnalyzerBuiState state)
    {
        _state = state;

        // Выбранная дуэль могла исчезнуть (рестарт) — откат на сводку.
        if (_selected != 0 && state.Duels.All(d => d.Number != _selected))
            _selected = 0;

        RebuildList();
        ShowSelected();
    }

    private void RebuildList()
    {
        _list.RemoveAllChildren();

        _list.AddChild(BuildEntryButton(0, Loc.GetString("fight-analyzer-session-summary")));

        if (_state.Duels.Count == 0)
        {
            _list.AddChild(new Label
            {
                Text = Loc.GetString("fight-analyzer-empty"),
                Margin = new Thickness(4, 10, 4, 4),
                HorizontalAlignment = Control.HAlignment.Center,
                StyleClasses = { "LabelSubText" },
            });
            return;
        }

        foreach (var duel in _state.Duels)
            _list.AddChild(BuildEntryButton(duel.Number, duel.Title));
    }

    private Control BuildEntryButton(int number, string title)
    {
        var button = new Button
        {
            Text = title,
            ToggleMode = true,
            Pressed = _selected == number,
            ClipText = true,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 2),
        };
        if (_selected == number)
            button.ModulateSelfOverride = AccentGreen;

        button.OnPressed += _ =>
        {
            _selected = number;
            RebuildList();
            ShowSelected();
        };
        return button;
    }

    private void ShowSelected()
    {
        var text = _selected == 0
            ? _state.SessionReport
            : _state.Duels.FirstOrDefault(d => d.Number == _selected)?.Report ?? _state.SessionReport;

        _report.SetMessage(FormattedMessage.FromMarkupPermissive(text));
    }
}
