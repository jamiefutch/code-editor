using Terminal.Gui.Editor;
using Terminal.Gui.Input;

namespace CodeEditor;

/// <summary>
///     Editor subclass with Visual Studio-style virtual space support.
///     Intercepts Right/Down at the key level so normal editing still works.
/// </summary>
public sealed class VirtualSpaceEditor : Editor
{
    private bool _enabled = true;

    /// <summary>
    ///     Whether virtual space is enabled. When false, Right/Down behave normally (snap to EOL).
    /// </summary>
    public bool VirtualSpaceEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
        }
    }

    public VirtualSpaceEditor() { }

    /// <summary>
    ///     Checks whether the caret is currently in virtual space (beyond end-of-line).
    /// </summary>
    public bool IsInVirtualSpace()
    {
        var doc = Document;
        if (doc is null) return false;

        var line = doc.GetLineByOffset(CaretOffset);
        var logicalColumn = CaretOffset - line.Offset;
        return logicalColumn > line.Length;
    }

    /// <summary>
    ///     Gets the virtual column offset (how many columns beyond EOL the cursor is).
    /// </summary>
    public int GetVirtualColumn()
    {
        var doc = Document;
        if (doc is null) return 0;

        var line = doc.GetLineByOffset(CaretOffset);
        var logicalColumn = CaretOffset - line.Offset;
        return Math.Max(0, logicalColumn - line.Length);
    }

    // ── Key-level intercept for virtual space ─────────────────────────────────────

    protected override bool OnKeyDown(Key key)
    {
        if (!_enabled)
            return base.OnKeyDown(key);

        if (key == Key.CursorRight || key == Key.CursorDown)
        {
            if (HandleVirtualSpaceKey(key))
                return true; // Handled.
        }

        return base.OnKeyDown(key);
    }

    private bool HandleVirtualSpaceKey(Key key)
    {
        var doc = Document;
        if (doc is null) return false;

        if (key == Key.CursorRight)
        {
            var line = doc.GetLineByOffset(CaretOffset);
            var eol = line.Offset + line.Length;

            // Only intercept when at or past end-of-line.
            if (CaretOffset < eol)
                return false;

            // Advance into virtual space by one column.
            CaretOffset++;
            return true;
        }

        if (key == Key.CursorDown)
        {
            var currentLine = doc.GetLineByOffset(CaretOffset);
            var logicalColumn = CaretOffset - currentLine.Offset;

            var nextLine = currentLine.NextLine;
            if (nextLine is null)
            {
                // Last line — move to end of document.
                CaretOffset = doc.TextLength;
                return true;
            }

            // Target offset on next line, preserving virtual column even past EOL.
            var targetOffset = nextLine.Offset + logicalColumn;
            CaretOffset = Math.Min(targetOffset, doc.TextLength);
            return true;
        }

        return false;
    }
}
