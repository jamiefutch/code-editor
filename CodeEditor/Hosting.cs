using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;
using Terminal.Gui;
using Terminal.Gui.Tracing;

namespace CodeEditor;

/// <summary>
///     Process-wide hosting concerns: Serilog → MEL → Terminal.Gui's Logging.Logger,
///     plus Trace.EnabledCategories. Extracted from Program.cs so tests can assert side effects.
/// </summary>
public static class Hosting
{
    /// <summary>Default trace categories for debugging menu/key/mouse flow.</summary>
    public const TraceCategory DefaultTraceCategories =
        TraceCategory.Command | TraceCategory.Keyboard | TraceCategory.Mouse;

    /// <summary>Default log file path. Daily rolling.</summary>
    public const string DefaultLogPath = "logs/codeeditor.log";

    /// <summary>
    ///     Configures Serilog → Microsoft.Extensions.Logging → Logging.Logger.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger ConfigureLogging(string? logPath = null)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath ?? DefaultLogPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        ILoggerFactory factory = LoggerFactory.Create(b =>
            b.AddSerilog(dispose: true).SetMinimumLevel(LogLevel.Trace));

        Microsoft.Extensions.Logging.ILogger logger = factory.CreateLogger("codeeditor");
        Terminal.Gui.App.Logging.Logger = logger;

        return logger;
    }

    /// <summary>Sets Trace.EnabledCategories.</summary>
    public static void EnableTracing(TraceCategory categories = DefaultTraceCategories)
        => Trace.EnabledCategories = categories;
}
