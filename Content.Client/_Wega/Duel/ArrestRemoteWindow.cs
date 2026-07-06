using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Wega.Duel;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Окно пульта конвоира: карточки гуманоидов на гриде с портретом, статусом и кнопкой «Задержать».
/// </summary>
public sealed class ArrestRemoteWindow : FancyWindow
{
    private static readonly Color ColorFree = Color.FromHex("#8bb8ff");
    private static readonly Color ColorHunted = Color.FromHex("#e0b64a");
    private static readonly Color ColorDown = Color.FromHex("#e08a4a");
    private static readonly Color ColorCuffed = Color.FromHex("#6fbf73");
    private static readonly Color ColorReady = Color.FromHex("#6fbf73");
    private static readonly Color ColorNoBot = Color.FromHex("#c94a4a");

    private static readonly Color CardBackground = Color.FromHex("#1f2027");
    private static readonly Color CardBorder = Color.FromHex("#3a3d4a");
    private static readonly Color HeaderBackground = Color.FromHex("#26283040");

    public event Action<NetEntity>? OnSelect;

    private readonly Label _status;
    private readonly Label _count;
    private readonly BoxContainer _list;

    public ArrestRemoteWindow()
    {
        Title = Loc.GetString("arrest-remote-window-title");
        MinSize = new Vector2(440, 540);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        root.AddChild(new Label
        {
            Text = Loc.GetString("arrest-remote-window-info"),
            Margin = new Thickness(2, 0, 2, 8),
        });

        // Шапка со статусом конвоира и счётчиком целей.
        _status = new Label { HorizontalExpand = true };
        _count = new Label { HorizontalAlignment = Control.HAlignment.Right };

        var headerRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };
        headerRow.AddChild(_status);
        headerRow.AddChild(_count);

        var header = new PanelContainer
        {
            Margin = new Thickness(0, 0, 0, 8),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = HeaderBackground,
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            },
        };
        header.AddChild(headerRow);
        root.AddChild(header);

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

    public void Populate(ArrestRemoteBuiState state, IEntityManager entMan)
    {
        _list.RemoveAllChildren();

        _status.Text = state.HasBot
            ? Loc.GetString("arrest-remote-window-ready")
            : Loc.GetString("arrest-remote-window-nobot");
        _status.FontColorOverride = state.HasBot ? ColorReady : ColorNoBot;
        _count.Text = Loc.GetString("arrest-remote-window-count", ("count", state.Targets.Count));

        if (state.Targets.Count == 0)
        {
            _list.AddChild(new Label
            {
                Text = Loc.GetString("arrest-remote-window-empty"),
                Margin = new Thickness(4, 12, 4, 4),
                HorizontalAlignment = Control.HAlignment.Center,
            });
            return;
        }

        foreach (var target in state.Targets)
        {
            _list.AddChild(BuildCard(target, state.HasBot, entMan));
        }
    }

    private Control BuildCard(ArrestRemoteTarget target, bool hasBot, IEntityManager entMan)
    {
        var card = new PanelContainer
        {
            Margin = new Thickness(0, 0, 0, 6),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = CardBackground,
                BorderColor = CardBorder,
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            },
        };

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        // Портрет цели, если она в поле зрения клиента.
        if (entMan.TryGetEntity(target.Entity, out var ent))
        {
            row.AddChild(new SpriteView(ent.Value, entMan)
            {
                OverrideDirection = Direction.South,
                SetSize = new Vector2(48, 48),
                VerticalAlignment = Control.VAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
        }
        else
        {
            row.AddChild(new Control { MinSize = new Vector2(48, 48), Margin = new Thickness(0, 0, 10, 0) });
        }

        var info = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = Control.VAlignment.Center,
        };
        info.AddChild(new Label
        {
            Text = target.Name,
            StyleClasses = { "LabelHeading" },
            ClipText = true,
        });
        info.AddChild(new Label
        {
            Text = target.Status,
            StyleClasses = { "LabelSubText" },
            FontColorOverride = StatusColor(target),
        });
        row.AddChild(info);

        var button = new Button
        {
            Text = Loc.GetString("arrest-remote-window-arrest"),
            Disabled = target.AlreadyTargeted || !hasBot,
            MinWidth = 104,
            VerticalAlignment = Control.VAlignment.Center,
        };

        var netEntity = target.Entity;
        button.OnPressed += _ => OnSelect?.Invoke(netEntity);
        row.AddChild(button);

        card.AddChild(row);
        return card;
    }

    private static Color StatusColor(ArrestRemoteTarget target)
    {
        var status = target.Status;
        if (status == Loc.GetString("arrest-remote-status-cuffed"))
            return ColorCuffed;
        if (status == Loc.GetString("arrest-remote-status-down"))
            return ColorDown;
        if (status == Loc.GetString("arrest-remote-status-hunted"))
            return ColorHunted;
        return ColorFree;
    }
}
