using System.Text;

namespace OrganizeContacts.Core.Diagnostics;

/// <summary>
/// Best-effort, local-only diagnostics. Entries are single-line and the file is
/// capped so a recurring crash cannot consume unbounded disk space.
/// </summary>
public sealed class LocalDiagnosticLog
{
    private const int MaxBytes = 1024 * 1024;
    private readonly object _gate = new();

    public LocalDiagnosticLog(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A log path is required.", nameof(filePath));
        FilePath = filePath;
    }

    public string FilePath { get; }
    public bool IsAvailable { get; private set; } = true;
    public string? LastError { get; private set; }

    public void Information(string category, string message) => Write(category, message);

    public void Error(string category, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write(category, $"{exception.GetType().FullName}: {exception.Message} {exception.StackTrace}");
    }

    public string ReadAll()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsAvailable = false;
            return string.Empty;
        }
    }

    private void Write(string category, string message)
    {
        if (!IsAvailable) return;

        var safeCategory = Sanitize(category);
        var safeMessage = Sanitize(message);
        var line = $"{DateTimeOffset.UtcNow:O} [{safeCategory}] {safeMessage}{Environment.NewLine}";

        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
                TrimIfOversize();
            }
            catch (Exception ex)
            {
                // Diagnostics must never become the source of an application failure.
                LastError = ex.Message;
                IsAvailable = false;
            }
        }
    }

    private void TrimIfOversize()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length <= MaxBytes) return;

        var bytes = File.ReadAllBytes(FilePath);
        var keep = bytes[^MaxBytes..];
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, keep);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
                // Best effort only; the next write can retry the trim.
            }
        }
    }

    private static string Sanitize(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}
