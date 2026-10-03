using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Chat;

/// <summary>Chat input with quiet spelling marks, preserving normal editing and history.</summary>
public sealed partial class SpellCheckingChatInput : HistoryLineEdit
{
    [Dependency] private IResourceCache _resources = default!;
    private readonly SpellingMarks _marks;
    private ChatSpellChecker? _checker;
    private string _checkedText = "";
    private int _checkedCursor = -1;
    private float _delay;
    private List<(int Start, int End)> _errors = new();

    public SpellCheckingChatInput()
    {
        IoCManager.InjectDependencies(this);
        _marks = new SpellingMarks(this);
        AddChild(_marks);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        _marks.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        return size;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_checkedText != Text || _checkedCursor != CursorPosition)
        {
            _checkedText = Text;
            _checkedCursor = CursorPosition;
            _errors.Clear();
            _delay = 0.35f;
        }
        if (_delay <= 0)
            return;
        _delay -= args.DeltaSeconds;
        if (_delay > 0 || Text.Length == 0)
            return;
        _checker ??= ChatSpellChecker.Get(_resources);
        _errors = _checker.FindErrors(Text, CursorPosition);
    }

    private sealed class SpellingMarks : Control
    {
        private readonly SpellCheckingChatInput _input;

        public SpellingMarks(SpellCheckingChatInput input)
        {
            _input = input;
            MouseFilter = MouseFilterMode.Ignore;
            RectClipContent = true;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            if (_input._errors.Count == 0)
                return;
            var font = _input.TryGetStyleProperty<Font>("font", out var styledFont)
                ? styledFont : UserInterfaceManager.ThemeDefaults.DefaultFont;
            var style = _input.StyleBoxOverride;
            if (style == null && !_input.TryGetStyleProperty<StyleBox>(LineEdit.StylePropertyStyleBox, out style))
                style = UserInterfaceManager.ThemeDefaults.LineEditBox;
            var box = style.GetContentBox(PixelSizeBox, UIScale);
            var y = Math.Min(box.Bottom - 1,
                box.Top + (box.Height - font.GetHeight(UIScale)) / 2 + font.GetAscent(UIScale) + 2);
            var x = _input.GetOffsetAtIndex(0) * UIScale;
            var errorIndex = 0;
            var startX = x;
            var index = 0;
            foreach (var rune in _input.Text.EnumerateRunes())
            {
                if (errorIndex >= _input._errors.Count || x >= box.Right)
                    break;
                var error = _input._errors[errorIndex];
                if (index == error.Start)
                    startX = x;
                if (font.TryGetCharMetrics(rune, UIScale, out var metrics))
                    x += metrics.Advance;
                index += rune.Utf16SequenceLength;
                if (index != error.End && !(index > error.Start && x >= box.Right))
                    continue;
                var left = Math.Max(startX, box.Left);
                var right = Math.Min(x, box.Right);
                if (right > left)
                    handle.DrawLine(new Vector2(left, y), new Vector2(right, y), new Color(0.8f, 0.32f, 0.32f, 0.65f));
                errorIndex++;
            }
        }
    }
}
