using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Document.Folding;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.Editor.Indentation;
using Terminal.Gui.Input;
using Terminal.Gui.Resources;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace CodeEditor;

/// <summary>
///     Top-level Window for CodeEditor. MenuBar at top, Editor in the middle,
///     StatusBar at the bottom. Partial class — file operations, edit commands,
///     and view settings are in separate files (matching ted's pattern).
/// </summary>
public sealed partial class CodeEditorApp : Window
{
    private readonly string _configPath;

    // View toggle CheckBoxes — stored so UpdateViewMenus can refresh their states.
    private CheckBox? _lineNumbersCheckBox;
    private CheckBox? _foldIndicatorsCheckBox;
    private CheckBox? _wordWrapCheckBox;
    private CheckBox? _showTabsCheckBox;
    private CheckBox? _scrollbarsCheckBox;
    private CheckBox? _virtualSpaceCheckBox;

    public VirtualSpaceEditor Editor { get; }
    public MenuBar Menu { get; }
    public EditorStatusBar StatusBar { get; }
    public string? CurrentFilePath { get; private set; }
    public bool IsDocumentModified => Editor.Document?.UndoStack.IsOriginalFile == false;

    // Dialog hooks for testability — initialized in constructor.
    public Func<string?> ShowOpenDialog { get; set; }
    public Func<string?> ShowSaveDialog { get; set; }
    public Func<SaveChangesChoice> ShowSaveChangesDialog { get; set; }

    public Shortcut LocShortcut => StatusBar.LocShortcut;
    public Shortcut LanguageShortcut => StatusBar.LanguageShortcut;
    public SpinnerView LoadStatusSpinner => StatusBar.LoadStatusSpinner;
    public Shortcut LoadSpinnerShortcut => StatusBar.LoadSpinnerShortcut;

    private long? _lastFileByteSize;
    private string _lastStatusVerb = "Loaded";
    private readonly Lock _streamingStatusLock = new();
    private long _lastStreamingStatusUnits;
    private DateTime _lastStreamingStatusUpdate = DateTime.MinValue;
    private long _streamingStatusOperationId;

    public Task<bool>? CurrentLoadTask { get; private set; }

    public CodeEditorApp(bool readOnly = false, string? configPath = null)
    {
        _configPath = configPath ?? EditorConfig.GetConfigPath();

        Title = "CodeEditor";
        BorderStyle = LineStyle.None;

        // ── Editor (VirtualSpaceEditor with VS-style virtual space) ──────────────────
        Editor = new()
        {
            ConvertTabsToSpaces = EditorConfig.ConvertTabsToSpaces,
            IndentationSize = EditorConfig.IndentSize,
            WordWrap = EditorConfig.WordWrap,
            ShowTabs = EditorConfig.ShowTabs,
            ReadOnly = readOnly,
            ViewportSettings = EditorConfig.Scrollbars
                ? ViewportSettingsFlags.HasScrollBars
                : ViewportSettingsFlags.None
        };

        // Gutter options.
        GutterOptions initialGutter = GutterOptions.None;
        if (EditorConfig.LineNumbers) initialGutter |= GutterOptions.LineNumbers;
        if (EditorConfig.FoldIndicators) initialGutter |= GutterOptions.Folding;
        Editor.GutterOptions = initialGutter;

        // Folding strategy.
        Editor.FoldingStrategy = new BraceFoldingStrategy();

        // Indentation strategy.
        Editor.IndentationStrategy = EditorConfig.AutoIndent ? new DefaultIndentationStrategy() : null;

        // ── MenuBar: custom-built with File/Edit/View (6 toggles incl. Virtual Space) + Help ──
        Menu = BuildMenuBar();

        // ── EditorStatusBar: pre-wired indicators ─────────────────────────────────────
        StatusBar = new(Editor);

        // ── Layout ────────────────────────────────────────────────────────────────────
        Editor.Y = Pos.Bottom(Menu);
        Editor.Width = Dim.Fill();
        Editor.Height = Dim.Fill(StatusBar);

        Add(Menu, Editor, StatusBar);

        // ── Editor events ─────────────────────────────────────────────────────────────
        Editor.ModifiedChanged += (_, _) => UpdateModifiedStatus();
        Editor.ContentChanged += (_, e) => UpdateContentSizeStatus(e);
        Editor.FindRequested += (_, _) => ShowFindReplaceDialog(false);
        Editor.ReplaceRequested += (_, _) => ShowFindReplaceDialog(true);

        // ── Initialize dialog hooks after Editor is created (App might not be ready yet) ──
        ShowOpenDialog = ShowDefaultOpenDialog;
        ShowSaveDialog = ShowDefaultSaveDialog;
        ShowSaveChangesDialog = ShowDefaultSaveChangesDialog;
    }

    // ── MenuBar construction ───────────────────────────────────────────────────────

    private MenuBar BuildMenuBar()
    {
        var menuBar = new MenuBar();

        // ── File menu ────────────────────────────────────────────────────────────────
        menuBar.Add(new MenuBarItem("_File", new View[]
        {
            new MenuItem { Title = Strings.cmdNew, Key = Key.N.WithCtrl, Action = New },
            new MenuItem { Title = Strings.cmdOpen, Key = Key.O.WithCtrl, Action = Open },
            new MenuItem { Title = Strings.cmdSave, Key = Key.S.WithCtrl, Action = Save },
            new MenuItem { Title = "Save _As", Key = Key.S.WithCtrl.WithShift, Action = () => _ = SaveFileAsAsync(null!, true) },
            new Line(),
            new MenuItem { Title = Strings.cmdQuit, Key = Key.Esc, Action = Quit }
        }));

        // ── Edit menu ────────────────────────────────────────────────────────────────
        menuBar.Add(new MenuBarItem("_Edit", new View[]
        {
            new MenuItem { Title = "_Find", Key = Key.F.WithCtrl, Action = () => ShowFindReplaceDialog(false) },
            new MenuItem { Title = "_Replace...", Key = Key.H.WithCtrl, Action = () => ShowFindReplaceDialog(true) },
            new Line(),
            new MenuItem { Title = "_Undo", Key = Key.Z.WithCtrl, Action = () => Editor.InvokeCommand(Command.Undo) },
            new MenuItem { Title = "_Redo", Key = Key.Y.WithCtrl, Action = () => Editor.InvokeCommand(Command.Redo) },
            new Line(),
            new MenuItem { Title = "Cu_t", Key = Key.X.WithCtrl, Action = () => Editor.InvokeCommand(Command.Cut) },
            new MenuItem { Title = "_Copy", Key = Key.C.WithCtrl, Action = () => Editor.InvokeCommand(Command.Copy) },
            new MenuItem { Title = "_Paste", Key = Key.V.WithCtrl, Action = () => Editor.InvokeCommand(Command.Paste) },
            new MenuItem { Title = "Delete", Key = Key.Delete, Action = () => Editor.InvokeCommand(Command.DeleteCharLeft) },
            new Line(),
            new MenuItem { Title = "Select _all", Key = Key.A.WithCtrl, Action = () => Editor.InvokeCommand(Command.SelectAll) }
        }));

        // ── View menu — 6 toggles using the TableEditor CheckBoxMenuItem pattern ─────
        menuBar.Add(new MenuBarItem("_View", BuildViewMenuItems()));

        // ── Help menu ────────────────────────────────────────────────────────────────
        menuBar.Add(new MenuBarItem("_Help", new View[] { new MenuItem("_About", "About CodeEditor", ShowAboutDialog) }));

        return menuBar;
    }

    private View[] BuildViewMenuItems()
    {
        _lineNumbersCheckBox = CreateToggleCheckBox(
            "_Line Numbers", EditorConfig.LineNumbers, ToggleLineNumbers);

        _foldIndicatorsCheckBox = CreateToggleCheckBox(
            "_Fold Indicators", EditorConfig.FoldIndicators, ToggleFoldIndicators);

        _wordWrapCheckBox = CreateToggleCheckBox(
            "_Word Wrap", EditorConfig.WordWrap, ToggleWordWrap);

        _showTabsCheckBox = CreateToggleCheckBox(
            "_Show Tabs", EditorConfig.ShowTabs, ToggleShowTabs);

        _scrollbarsCheckBox = CreateToggleCheckBox(
            "_Scrollbars", EditorConfig.Scrollbars, ToggleScrollbars);

        _virtualSpaceCheckBox = CreateToggleCheckBox(
            "_Virtual Space", EditorConfig.VirtualSpace, ToggleVirtualSpace);

        // Return MenuItem wrappers with CommandView — Terminal.Gui needs MenuItems
        // for the submenu to render and open correctly.
        return new View[]
        {
            (View)new MenuItem { CommandView = _lineNumbersCheckBox },
            (View)new MenuItem { CommandView = _foldIndicatorsCheckBox },
            (View)new MenuItem { CommandView = _wordWrapCheckBox },
            (View)new MenuItem { CommandView = _showTabsCheckBox },
            (View)new MenuItem { CommandView = _scrollbarsCheckBox },
            (View)new MenuItem { CommandView = _virtualSpaceCheckBox }
        };
    }

    /// <summary>
    ///     Creates a CheckBox menu item matching the TableEditor / EditorMenuBar pattern.
    ///     The CheckBox uses Title (not Text) for menu rendering, and Accepting advances
    ///     its state when clicked in the menu.
    /// </summary>
    private static CheckBox CreateToggleCheckBox(string title, bool initialState, Action onToggle)
    {
        var checkBox = new CheckBox() { Title = title, Value = initialState ? CheckState.Checked : CheckState.UnChecked };

        // Update persisted state when value changes.
        checkBox.ValueChanged += (_, _) => onToggle();

        // Advance the check state when the menu item is activated (clicked).
        checkBox.Accepting += (_, e) =>
        {
            checkBox.AdvanceCheckState();
            e.Handled = true;
        };

        return checkBox;
    }

    private void UpdateViewMenus()
    {
        if (_lineNumbersCheckBox is not null)
        {
            bool has = Editor.GutterOptions.HasFlag(GutterOptions.LineNumbers);
            _lineNumbersCheckBox.Value = has ? CheckState.Checked : CheckState.UnChecked;
        }
        if (_foldIndicatorsCheckBox is not null)
        {
            bool has = Editor.GutterOptions.HasFlag(GutterOptions.Folding);
            _foldIndicatorsCheckBox.Value = has ? CheckState.Checked : CheckState.UnChecked;
        }
        if (_wordWrapCheckBox is not null)
            _wordWrapCheckBox.Value = Editor.WordWrap ? CheckState.Checked : CheckState.UnChecked;
        if (_showTabsCheckBox is not null)
            _showTabsCheckBox.Value = Editor.ShowTabs ? CheckState.Checked : CheckState.UnChecked;
        if (_scrollbarsCheckBox is not null)
        {
            bool has = Editor.ViewportSettings.HasFlag(ViewportSettingsFlags.HasScrollBars);
            _scrollbarsCheckBox.Value = has ? CheckState.Checked : CheckState.UnChecked;
        }
        if (_virtualSpaceCheckBox is not null)
            _virtualSpaceCheckBox.Value = Editor.VirtualSpaceEnabled ? CheckState.Checked : CheckState.UnChecked;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private static string ToggleTitle(bool on, string label)
        => on ? $"✓ {label}" : $"  {label}";

    private void UpdateModifiedStatus()
    {
        if (LoadStatusSpinner.AutoSpin) return;

        var verb = Editor.IsModified ? "Modified" : _lastStatusVerb;
        var status = FormatCompletedProgress(verb, _lastFileByteSize);
        LoadSpinnerShortcut.Title = status;
        LoadSpinnerShortcut.HelpText = status;
        LoadSpinnerShortcut.SetNeedsDraw();
    }

    private void UpdateContentSizeStatus(DocumentChangeEventArgs e)
    {
        if (LoadStatusSpinner.AutoSpin) return;
        if (_lastFileByteSize is not { } currentSize) return;

        Encoding encoding = Editor.Document?.Encoding ?? Encoding.UTF8;
        long insertedBytes = encoding.GetByteCount(e.InsertedText.Text);
        long removedBytes = encoding.GetByteCount(e.RemovedText.Text);
        _lastFileByteSize = Math.Max(0, currentSize + insertedBytes - removedBytes);

        UpdateModifiedStatus();
    }

    private void SetLoadStatus(string status, bool showSpinner, long statusOperationId)
    {
        void Update()
        {
            if (Interlocked.Read(ref _streamingStatusOperationId) != statusOperationId) return;
            LoadStatusSpinner.Visible = showSpinner;
            LoadStatusSpinner.AutoSpin = showSpinner;
            LoadSpinnerShortcut.Title = status;
            LoadSpinnerShortcut.HelpText = status;
            LoadStatusSpinner.SetNeedsDraw();
            LoadSpinnerShortcut.SetNeedsDraw();
        }

        if (App is null) { Update(); return; }
        App.Invoke(Update);
    }

    private long BeginStreamingStatus(string status)
    {
        var id = Interlocked.Increment(ref _streamingStatusOperationId);
        ResetStreamingStatusThrottle();
        SetLoadStatus(status, true, id);
        return id;
    }

    private void CompleteStreamingStatus(long statusOperationId, string status)
    {
        var completionId = statusOperationId + 1;
        if (Interlocked.CompareExchange(ref _streamingStatusOperationId, completionId, statusOperationId) != statusOperationId) return;
        SetLoadStatus(status, false, completionId);
    }

    private void CompleteStreamingStatus(string status)
    {
        var id = Interlocked.Increment(ref _streamingStatusOperationId);
        SetLoadStatus(status, false, id);
    }

    private void ResetStreamingStatusThrottle()
    {
        lock (_streamingStatusLock)
        {
            _lastStreamingStatusUpdate = DateTime.MinValue;
            _lastStreamingStatusUnits = 0;
        }
    }

    private bool ShouldReportStreamingProgress(long statusOperationId, long processed, long total)
    {
        if (Interlocked.Read(ref _streamingStatusOperationId) != statusOperationId) return false;
        if (total == processed) return true;

        lock (_streamingStatusLock)
        {
            DateTime now = DateTime.UtcNow;
            if (processed - _lastStreamingStatusUnits < 256 * 1024 && now - _lastStreamingStatusUpdate < TimeSpan.FromMilliseconds(100)) return false;
            _lastStreamingStatusUnits = processed;
            _lastStreamingStatusUpdate = now;
        }
        return true;
    }

    private static string FormatProgress(string verb, long processed, long? total)
    {
        var p = FormatByteCount(processed);
        if (total is null) return $"{verb} {p}";
        var t = FormatByteCount(total.Value);
        return $"{verb} {p} of {t}";
    }

    private static string FormatStartingProgress(string verb, long? totalBytes)
        => totalBytes is { } b ? $"{verb} 0 B of {FormatByteCount(b)}" : $"{verb} 0 B";

    private static string FormatCompletedProgress(string verb, long? totalBytes)
        => totalBytes is { } b ? $"{verb} {FormatByteCount(b)}" : verb;

    private static string FormatByteCount(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value.ToString(i == 0 ? "N0" : "N1")} {units[i]}";
    }

    private static long? GetStreamLength(Stream stream)
        => stream.CanSeek ? stream.Length : null;
}
