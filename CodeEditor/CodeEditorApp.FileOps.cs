using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.Resources;
using Terminal.Gui.Views;

namespace CodeEditor;

public sealed partial class CodeEditorApp
{
    private const long StreamingStatusInterval = 256 * 1024;
    private const int StreamingStatusMilliseconds = 100;

    // ── New ─────────────────────────────────────────────────────────────────────────

    public void NewFile()
    {
        SetDocument(string.Empty, null);
    }

    private void New()
    {
        if (!ConfirmSaveChanges()) return;
        NewFile();
    }

    // ── Open ────────────────────────────────────────────────────────────────────────

    public bool OpenFile()
    {
        var filePath = ShowOpenDialog();
        return !string.IsNullOrWhiteSpace(filePath) && RunSyncBridge(() => OpenFileAsync(filePath));
    }

    internal bool OpenFileBlocking(string filePath)
        => RunSyncBridge(() => OpenFileAsync(filePath));

    /// <summary>Opens a CLI-requested missing file path as an empty, modified document.</summary>
    public void OpenMissingFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Path must not be null, empty, or whitespace.", nameof(filePath));

        SetDocument(string.Empty, filePath);
        Editor.Document ??= new TextDocument();
        Editor.Document.UndoStack.DiscardOriginalFileMarker();
    }

    private void Open()
    {
        if (!ConfirmSaveChanges()) return;
        var filePath = ShowOpenDialog();
        if (string.IsNullOrWhiteSpace(filePath)) return;
        CurrentLoadTask = OpenFileAsync(filePath, true);
    }

    internal async Task<bool> OpenFileAsync(string filePath, bool marshalToApp = false, CancellationToken cancellationToken = default)
    {
        long? statusOperationId = null;

        try
        {
            await using Stream stream = File.OpenRead(filePath);
            var fileSize = GetStreamLength(stream);
            var startedId = BeginStreamingStatus(FormatStartingProgress("Loading", fileSize));
            statusOperationId = startedId;

            IProgress<TextDocumentProgress> progress = CreateStreamingProgress(p => ReportLoadProgress(startedId, p));

            Func<Action, Task>? marshal = marshalToApp ? InvokeOnAppAsync : null;

            await Editor.LoadAsync(stream, null, progress, cancellationToken, marshal);

            await RunOnApp(marshalToApp, () =>
            {
                CurrentFilePath = filePath;
                ApplyFileMetadata(filePath);
                _lastFileByteSize = fileSize;
                _lastStatusVerb = "Loaded";
                CompleteStreamingStatus(startedId, FormatCompletedProgress("Loaded", fileSize));
            });

            if (!marshalToApp) Editor.Document?.SetOwnerThread(null);

            return true;
        }
        catch (OperationCanceledException)
        {
            if (statusOperationId is { } id) CompleteStreamingStatus(id, "Load canceled");
            else CompleteStreamingStatus("Load canceled");
            return false;
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            if (statusOperationId is { } id) CompleteStreamingStatus(id, "Load failed");
            else CompleteStreamingStatus("Load failed");
            return false;
        }
    }

    public void BeginOpenFile(string filePath) => CurrentLoadTask = OpenFileAsync(filePath, true);

    private void ReportLoadProgress(long statusOperationId, TextDocumentProgress progress)
    {
        var processed = progress.BytesProcessed ?? progress.CharactersProcessed;
        var total = progress.TotalBytes ?? progress.TotalCharacters ?? 0;
        if (!ShouldReportStreamingProgress(statusOperationId, processed, total)) return;
        SetLoadStatus(FormatProgress("Loading", processed, total), true, statusOperationId);
    }

    // ── Save / Save As ──────────────────────────────────────────────────────────────

    public bool SaveFile() => CurrentFilePath is null ? SaveFileAs() : RunSyncBridge(() => SaveFileAsync());

    private async Task<bool> SaveFileAsync(bool marshalToApp = false, CancellationToken cancellationToken = default)
    {
        if (CurrentFilePath is null) return await SaveFileAsAsync(null!, marshalToApp, cancellationToken);

        try
        {
            await SaveFileToAsync(CurrentFilePath, marshalToApp, cancellationToken);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (IsFileOperationException(ex)) { return false; }

        return true;
    }

    private void Save() => _ = SaveFileAsync(true);

    public bool SaveFileAs()
    {
        var filePath = ShowSaveDialog();
        return !string.IsNullOrWhiteSpace(filePath) && RunSyncBridge(() => SaveFileAsAsync(filePath));
    }

    private async Task<bool> SaveFileAsAsync(string filePath, bool marshalToApp = false, CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveFileToAsync(filePath, marshalToApp, cancellationToken);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (IsFileOperationException(ex)) { return false; }

        CurrentFilePath = filePath;
        UpdateFileNameShortcut();
        return true;
    }

    private async Task SaveFileToAsync(string filePath, bool marshalToApp, CancellationToken cancellationToken)
    {
        long? statusOperationId = null;

        try
        {
            await using Stream stream = File.Create(filePath);
            var startedId = BeginStreamingStatus(FormatStartingProgress("Saving", null));
            statusOperationId = startedId;

            IProgress<TextDocumentProgress> progress = CreateStreamingProgress(p => ReportSaveProgress(startedId, p));
            await Editor.SaveAsync(stream, progress, cancellationToken);
            var fileSize = GetStreamLength(stream);

            void MarkSaved()
            {
                _lastFileByteSize = fileSize;
                _lastStatusVerb = "Saved";
                Editor.Document!.UndoStack.MarkAsOriginalFile();
                CompleteStreamingStatus(startedId, FormatCompletedProgress("Saved", fileSize));
            }

            if (marshalToApp) await InvokeOnAppAsync(MarkSaved);
            else MarkSaved();
        }
        catch (OperationCanceledException)
        {
            if (statusOperationId is { } id) CompleteStreamingStatus(id, "Save canceled");
            else CompleteStreamingStatus("Save canceled");
            throw;
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            if (statusOperationId is { } id) CompleteStreamingStatus(id, "Save failed");
            else CompleteStreamingStatus("Save failed");
            throw;
        }
    }

    private void ReportSaveProgress(long statusOperationId, TextDocumentProgress progress)
    {
        var processed = progress.BytesProcessed ?? progress.CharactersProcessed;
        var total = progress.TotalBytes ?? progress.TotalCharacters ?? 0;
        if (!ShouldReportStreamingProgress(statusOperationId, processed, total)) return;
        SetLoadStatus(FormatProgress("Saving", processed, total), true, statusOperationId);
    }

    // ── Quit ────────────────────────────────────────────────────────────────────────

    public bool QuitFile()
    {
        if (!ConfirmSaveChanges()) return false;
        RequestStop();
        return true;
    }

    private void Quit() => QuitFile();

    // ── Confirm save changes ────────────────────────────────────────────────────────

    private bool ConfirmSaveChanges()
    {
        if (!IsDocumentModified) return true;
        return ShowSaveChangesDialog() switch
        {
            SaveChangesChoice.Discard => true,
            SaveChangesChoice.Save => SaveFile(),
            _ => false
        };
    }

    // ── Document management ─────────────────────────────────────────────────────────

    private void SetDocument(string text, string? filePath)
    {
        Editor.ClearSelection();
        Editor.Document = new TextDocument(text);
        Editor.CaretOffset = 0;
        CurrentFilePath = filePath;

        Encoding encoding = Editor.Document?.Encoding ?? Encoding.UTF8;
        _lastFileByteSize = text.Length == 0 ? 0 : encoding.GetByteCount(text);
        _lastStatusVerb = "Loaded";
        CompleteStreamingStatus(FormatCompletedProgress("Loaded", _lastFileByteSize));

        ApplyFileMetadata(filePath);
    }

    private void ApplyFileMetadata(string? filePath)
    {
        IHighlightingDefinition? def = null;
        if (filePath is not null)
        {
            var ext = Path.GetExtension(filePath);
            if (!string.IsNullOrEmpty(ext))
                def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        }

        Editor.HighlightingDefinition = def;
        LanguageShortcut.Title = def?.Name ?? "Plain Text";
        UpdateFileNameShortcut();
        Editor.SetNeedsDraw();
    }

    // ── Dialog hooks (default implementations) ──────────────────────────────────────

    private string? ShowDefaultOpenDialog()
    {
        using OpenDialog dialog = new()
        {
            AllowsMultipleSelection = false,
            MustExist = true,
            OpenMode = OpenMode.File
        };

        if (App is null) throw new InvalidOperationException("Application not running.");
        App.Run(dialog);
        return dialog.FilePaths.FirstOrDefault();
    }

    private string? ShowDefaultSaveDialog()
    {
        using SaveDialog dialog = CreateSaveDialog();
        if (App is null) throw new InvalidOperationException("Application not running.");
        App.Run(dialog);
        return dialog.FileName;
    }

    internal SaveDialog CreateSaveDialog()
        => new() { Title = Strings.fdSaveAs, AllowsMultipleSelection = false, OpenMode = OpenMode.File };

    private SaveChangesChoice ShowDefaultSaveChangesDialog()
    {
        if (App is null) throw new InvalidOperationException("Application not running.");
        var result = MessageBox.Query(App, "Save changes?", "The document has unsaved changes. Save before quitting?", Strings.btnCancel, Strings.btnNo, Strings.btnYes);
        return result switch
        {
            1 => SaveChangesChoice.Discard,
            2 => SaveChangesChoice.Save,
            _ => SaveChangesChoice.Cancel
        };
    }

    // ── Sync bridge (avoids deadlocking on Terminal.Gui's SynchronizationContext) ────

    private bool RunSyncBridge(Func<Task<bool>> operation)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return operation().GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private async Task RunOnApp(bool marshalToApp, Action action)
    {
        if (marshalToApp) await InvokeOnAppAsync(action);
        else action();
    }

    private Task InvokeOnAppAsync(Action action)
    {
        if (App is null) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource();
        App.Invoke(() => { try { action(); tcs.SetResult(); } catch (Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

    // ── Streaming progress helpers ──────────────────────────────────────────────────

    private IProgress<TextDocumentProgress> CreateStreamingProgress(Action<TextDocumentProgress> handler)
        => App is null ? (IProgress<TextDocumentProgress>)new InlineProgress<TextDocumentProgress>(handler) : new Progress<TextDocumentProgress>(handler);

    // ── File operation exception filter ─────────────────────────────────────────────

    private static bool IsFileOperationException(Exception ex)
        => ex is IOException or UnauthorizedAccessException;
}
