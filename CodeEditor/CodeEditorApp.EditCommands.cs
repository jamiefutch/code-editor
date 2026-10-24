using Terminal.Gui.Editor;

namespace CodeEditor;

public sealed partial class CodeEditorApp
{
    private void ShowFindReplaceDialog(bool selectReplaceTab)
    {
        if (App is null) throw new InvalidOperationException("Cannot show find/replace when Application is not running.");

        using var dialog = new FindReplaceDialog(Editor, selectReplaceTab);
        App.Run(dialog);
    }
}
