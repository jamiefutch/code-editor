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
    private readonly Shortcut _fileNameShortcut;

    public VirtualSpaceEditor Editor { get; }
    public EditorMenuBar Menu { get; }
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

        // ── EditorMenuBar: pre-wired File/Edit/View menus ────────────────────────────
        Menu = new(Editor)
        {
            ShowOpenDialog = () => ShowOpenDialog!(),
            ShowSaveDialog = () => ShowSaveDialog!()
        };

        Menu.NewRequested += (_, _) => New();
        Menu.Opening += (_, e) => { if (!ConfirmSaveChanges()) e.Cancel = true; };
        Menu.OpenRequested += (_, e) => CurrentLoadTask = OpenFileAsync(e.FilePath, true);
        Menu.SaveRequested += (_, _) => Save();
        Menu.SaveAsRequested += (_, e) => { _ = SaveFileAsAsync(e.FilePath, true); };
        Menu.QuitRequested += (_, _) => Quit();
        Menu.ViewSettingsChanged += (_, _) => SaveViewSettings();

        // Add Help menu.
        Menu.Add(new MenuBarItem("_Help", new[] { new MenuItem("_About", "About CodeEditor", ShowAboutDialog) }));

        // File-name shortcut in the status bar area (shown in Menu).
        _fileNameShortcut = new Shortcut(Key.Empty, "<untitled>", Open)
        {
            MouseHighlightStates = MouseState.None,
            SchemeName = SchemeManager.SchemesToSchemeName(Schemes.Dialog)
        };
        Menu.Add(_fileNameShortcut);

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

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private static string ToggleTitle(bool on, string label)
        => on ? $"✓ {label}" : $"  {label}";

    private void UpdateFileNameShortcut()
    {
        _fileNameShortcut.Title = CurrentFilePath ?? "<untitled>";
        _fileNameShortcut.SetNeedsDraw();
    }

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
