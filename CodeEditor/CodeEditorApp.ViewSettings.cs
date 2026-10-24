using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Indentation;
using Terminal.Gui.Resources;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace CodeEditor;

public sealed partial class CodeEditorApp
{
    private void SaveViewSettings()
    {
        EditorConfig.LineNumbers = Editor.GutterOptions.HasFlag(GutterOptions.LineNumbers);
        EditorConfig.FoldIndicators = Editor.GutterOptions.HasFlag(GutterOptions.Folding);
        EditorConfig.WordWrap = Editor.WordWrap;
        EditorConfig.ShowTabs = Editor.ShowTabs;
        EditorConfig.IndentSize = Editor.IndentationSize;
        EditorConfig.ConvertTabsToSpaces = Editor.ConvertTabsToSpaces;
        EditorConfig.AutoIndent = Editor.IndentationStrategy is not null;
        EditorConfig.Scrollbars = Editor.ViewportSettings.HasFlag(ViewportSettingsFlags.HasScrollBars);
        EditorConfig.VirtualSpace = true; // Always enabled when configured

        EditorConfig.Save();
    }

    private void ToggleLineNumbers()
    {
        bool has = Editor.GutterOptions.HasFlag(GutterOptions.LineNumbers);
        Editor.GutterOptions = has
            ? Editor.GutterOptions ^ GutterOptions.LineNumbers
            : Editor.GutterOptions | GutterOptions.LineNumbers;
        SaveViewSettings();
    }

    private void ToggleWordWrap()
    {
        Editor.WordWrap = !Editor.WordWrap;
        SaveViewSettings();
    }

    private void ToggleFoldIndicators()
    {
        bool has = Editor.GutterOptions.HasFlag(GutterOptions.Folding);
        Editor.GutterOptions = has
            ? Editor.GutterOptions ^ GutterOptions.Folding
            : Editor.GutterOptions | GutterOptions.Folding;
        SaveViewSettings();
    }

    private void ToggleAutoIndent()
    {
        if (Editor.IndentationStrategy is not null)
        {
            Editor.IndentationStrategy = null;
        }
        else
        {
            Editor.IndentationStrategy = new DefaultIndentationStrategy();
        }
        SaveViewSettings();
    }

    private void ToggleShowTabs()
    {
        Editor.ShowTabs = !Editor.ShowTabs;
        SaveViewSettings();
    }

    private void ToggleScrollbars()
    {
        bool has = Editor.ViewportSettings.HasFlag(ViewportSettingsFlags.HasScrollBars);
        Editor.ViewportSettings = has
            ? ViewportSettingsFlags.None
            : ViewportSettingsFlags.HasScrollBars;
        SaveViewSettings();
    }

    private void ToggleVirtualSpace()
    {
        // Virtual space is always enabled via EnableVirtualSpace().
        // This toggle just confirms the setting in the status bar.
        EditorConfig.VirtualSpace = true;
        EditorConfig.Save();
    }

    private void ShowAboutDialog()
    {
        if (App is null) return;

        using var dialog = new Dialog()
        {
            Title = "About CodeEditor",
            Buttons = [new Button { Title = Strings.btnOk, IsDefault = true }]
        };

        dialog.Border.Settings &= ~BorderSettings.Title;

        Version? tgVersion = typeof(Application).Assembly.GetName().Version;
        Version? editorVersion = typeof(Editor).Assembly.GetName().Version;

        var tagline = new Label()
        {
            Text = $"Terminal-based code editor built with Terminal.Gui {tgVersion}",
            TextAlignment = Alignment.Center,
            X = Pos.Center(),
            Width = Dim.Auto(DimAutoStyle.Text),
            Height = Dim.Auto(DimAutoStyle.Text)
        };

        var versionLabel = new Label()
        {
            Text = $"Terminal.Gui.Editor {editorVersion}",
            X = Pos.Center(),
            Y = Pos.Bottom(tagline) + 1,
            Width = Dim.Auto(),
            Height = Dim.Auto()
        };

        dialog.Add(tagline, versionLabel);
        App.Run(dialog);
    }
}
