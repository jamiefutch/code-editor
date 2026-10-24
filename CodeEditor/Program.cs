// CodeEditor — terminal-based code editor built on Terminal.Gui.Editor.
// Top-level statements with .NET 10 / C# 14 patterns.

using Terminal.Gui.App;

CodeEditor.Hosting.ConfigureLogging();
CodeEditor.Hosting.EnableTracing();

CodeEditor.EditorConfig.LoadAndApply();

using IApplication app = Application.Create();
app.Init();

var readOnly = args.Any(static arg => arg is "--read-only" or "-r");
var requestedPath = args.FirstOrDefault(static arg => arg is not ("--read-only" or "-r"));

using var editor = new CodeEditor.CodeEditorApp(readOnly);

if (!string.IsNullOrWhiteSpace(requestedPath))
{
    FileInfo file = new(requestedPath);

    if (!file.Exists)
    {
        app.Invoke(() => editor.OpenMissingFile(requestedPath));
    }
    else if (file.Length <= 1024 * 1024)
    {
        // Small files: synchronous load before first paint.
        editor.OpenFileBlocking(requestedPath);
    }
    else
    {
        // Large files: progressive streaming, window appears immediately.
        app.Invoke(() => editor.BeginOpenFile(requestedPath));
    }
}

app.Run(editor);
