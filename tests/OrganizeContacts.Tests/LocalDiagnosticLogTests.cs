using OrganizeContacts.Core.Diagnostics;

namespace OrganizeContacts.Tests;

public sealed class LocalDiagnosticLogTests
{
    [Fact]
    public void Writes_single_line_entries_and_reads_them_back()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"oc-diagnostics-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "diagnostics.log");
        try
        {
            var log = new LocalDiagnosticLog(path);
            log.Information("test", "first line\nsecond line");
            log.Error("failure", new InvalidOperationException("boom\nwith newline"));

            var lines = log.ReadAll().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.DoesNotContain('\n', line));
            Assert.Contains("[test] first line second line", lines[0]);
            Assert.Contains("[failure] System.InvalidOperationException: boom with newline", lines[1]);
            Assert.True(log.IsAvailable);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Caps_log_size_after_large_entry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-diagnostics-{Guid.NewGuid():N}.log");
        try
        {
            var log = new LocalDiagnosticLog(path);
            log.Information("large", new string('x', 1_100_000));

            Assert.True(new FileInfo(path).Length <= 1024 * 1024);
            Assert.True(log.IsAvailable);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
