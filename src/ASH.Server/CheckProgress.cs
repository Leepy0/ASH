namespace ASH.Server;

/// <summary>
/// Where the GitHub check is right now (the button and the scheduled check alike): fixed steps, each 대기/진행/완료/건너뜀/실패 with a short
/// result, and for downloads the bytes received. Thread-safe; the settings page polls <see cref="Snapshot"/>.
/// </summary>
public sealed class CheckProgress
{
    public const int PluginCheck = 0, PluginGet = 1, RunnerCheck = 2, RunnerGet = 3, Notes = 4, ServerCheck = 5, ServerGet = 6, ServerPrepare = 7;
    public const string Wait = "대기", Active = "진행", Done = "완료", Skipped = "건너뜀", Failed = "실패";
    private static readonly (string Name, string Doing)[] Defs =
    [
        ("플러그인 확인", "플러그인 확인 중"), ("플러그인 받기", "플러그인 받는 중"),
        ("Runner 확인", "Runner 확인 중"), ("Runner 받기", "Runner 받는 중"),
        ("공지 받기", "공지 받는 중"),
        ("서버 확인", "서버 확인 중"), ("서버 받기", "서버 받는 중"), ("서버 준비", "서버 압축 푸는 중"),
    ];

    private sealed class S { public string State = Wait, Text = ""; public long Bytes, Total; }

    private readonly object _lock = new();
    private readonly S[] _s = Enumerable.Range(0, Defs.Length).Select(_ => new S()).ToArray();
    private bool _running;
    private int _run;
    private DateTime? _started, _finished;

    public bool Running { get { lock (_lock) return _running; } }

    /// <summary>Claims the check: false when one is already running. Resets every step.</summary>
    public bool TryBegin()
    {
        lock (_lock)
        {
            if (_running) return false;
            _running = true; _run++; _started = DateTime.UtcNow; _finished = null;
            foreach (var s in _s) { s.State = Wait; s.Text = ""; s.Bytes = s.Total = 0; }
            return true;
        }
    }

    /// <summary>The check is over: a step still waiting did not apply (not configured, nothing to get) and one still active was cut off.</summary>
    public void End()
    {
        lock (_lock)
        {
            foreach (var s in _s) { if (s.State == Wait) s.State = Skipped; else if (s.State == Active) { s.State = Failed; s.Text = "중단"; } }
            _running = false; _finished = DateTime.UtcNow;
        }
    }

    public void Start(int i, long total = 0) { lock (_lock) { _s[i].State = Active; _s[i].Total = total; _s[i].Bytes = 0; } }
    public void Finish(int i, string text) { lock (_lock) { _s[i].State = Done; _s[i].Text = text; } }
    public void Skip(int i, string text) { lock (_lock) { _s[i].State = Skipped; _s[i].Text = text; } }
    public void Bytes(int i, long n) { lock (_lock) _s[i].Bytes = n; }

    /// <summary>The outcome text of a group of steps (from..to): the active one finishes with it (or fails when it says 실패/거절), the ones not reached are skipped.</summary>
    public void Settle(int from, int to, string text)
    {
        var bad = text.StartsWith("실패") || text.StartsWith("거절");
        lock (_lock)
            for (int i = from; i <= to; i++)
            {
                var s = _s[i];
                if (s.State == Active) { s.State = bad ? Failed : Done; s.Text = text; }
                else if (s.State == Wait) s.State = Skipped;
            }
    }

    /// <summary>The JSON body of GET /admin/check-updates/status.</summary>
    public object Snapshot()
    {
        lock (_lock)
            return new
            {
                running = _running, run = _run, started = _started, finished = _finished,
                steps = _s.Select((s, i) => new { name = Defs[i].Name, doing = Defs[i].Doing, state = s.State, text = s.Text, bytes = s.Bytes, total = s.Total }).ToArray(),
            };
    }
}

/// <summary>Read-through wrapper that reports the bytes read so far; what is read, verified and stored stays the same.</summary>
internal sealed class CountingStream(Stream inner, Action<long> onBytes) : Stream
{
    private long _n;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => _n; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Count(await inner.ReadAsync(buffer, ct));
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Count(await inner.ReadAsync(buffer.AsMemory(offset, count), ct));
    private int Count(int n) { if (n > 0) onBytes(_n += n); return n; }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
