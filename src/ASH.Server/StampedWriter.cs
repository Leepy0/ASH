using System.Globalization;
using System.Text;

namespace ASH.Server;

/// <summary>Writes "[yyyy-MM-dd HH:mm:ss.fff] " (Seoul) in front of every line, so the gaps between lines of job.log / film.log show where the time went.</summary>
public sealed class StampedWriter(TextWriter inner) : TextWriter
{
    readonly object _gate = new();
    bool _lineStart = true;

    public override Encoding Encoding => inner.Encoding;

    public static string Stamp() => ASH.Core.SeoulTime.FromUtc(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public override void Write(char value)
    {
        lock (_gate)
        {
            if (_lineStart && value != '\r' && value != '\n') { inner.Write('['); inner.Write(Stamp()); inner.Write("] "); }
            inner.Write(value);
            _lineStart = value == '\n';
        }
    }

    public override void Write(string? value) { if (value != null) lock (_gate) foreach (var c in value) Write(c); }
    public override void WriteLine(string? value) { lock (_gate) { Write(value); Write('\n'); } }
    public override void WriteLine() => Write('\n');
    public override void Flush() => inner.Flush();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
