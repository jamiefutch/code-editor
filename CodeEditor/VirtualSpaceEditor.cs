using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace CodeEditor;

/// <summary>
///     Editor subclass with Visual Studio-style virtual space support.
///     Allows cursor to move beyond end-of-line on short lines via Right/Down arrows.
/// </summary>
public sealed class VirtualSpaceEditor : Editor
{
    public VirtualSpaceEditor()
    {
        // Replace Command.Right handler to allow moving past EOL into virtual space.
        AddCommand(Command.Right, VirtualSpaceRight);

        // Replace Command.Down handler to preserve virtual column across short lines.
        AddCommand(Command.Down, VirtualSpaceDown);
    }

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

    // ── Virtual space command handlers ────────────────────────────────────────────

    private bool? VirtualSpaceRight(ICommandContext? ctx)
    {
        var doc = Document;
        if (doc is null) return null;

        var line = doc.GetLineByOffset(CaretOffset);
        var eol = line.Offset + line.Length;

        if (CaretOffset < eol)
        {
            // Not yet at EOL — fall through to default handler.
            return null;
        }

        // We're at or past EOL — advance into virtual space by one column.
        CaretOffset = CaretOffset + 1;
        return true;
    }

    private bool? VirtualSpaceDown(ICommandContext? ctx)
    {
        var doc = Document;
        if (doc is null) return null;

        var currentLine = doc.GetLineByOffset(CaretOffset);
        var logicalColumn = CaretOffset - currentLine.Offset;

        // Move to the next line (or stay if we're on the last line).
        var nextLine = currentLine.NextLine;
        if (nextLine is null)
        {
            // Last line — move to end of document.
            CaretOffset = doc.TextLength;
            return true;
        }

        // Calculate the target offset on the next line, clamped to virtual column.
        var nextLineStart = nextLine.Offset;
        int targetOffset;

        if (logicalColumn <= nextLine.Length)
        {
            // Next line is long enough — move to that column.
            targetOffset = nextLineStart + logicalColumn;
        }
        else
        {
            // Next line is shorter — go into virtual space.
            targetOffset = nextLineStart + logicalColumn;
        }

        CaretOffset = Math.Min(targetOffset, doc.TextLength);
        return true;
    }
}
