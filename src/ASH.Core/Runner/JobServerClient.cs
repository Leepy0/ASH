using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASH.Core.Runner;

/// <summary>GET /api/jobs/queue: how many jobs wait, when a new one would probably start, and whether the server runs jobs at all.</summary>
[DataContract]
public sealed class QueueInfo
{
    [DataMember(Name = "ahead")] public int Ahead { get; set; }
    /// <summary>ISO-8601 UTC, or null when the server cannot tell.</summary>
    [DataMember(Name = "estimatedStart")] public string? EstimatedStart { get; set; }
    [DataMember(Name = "typicalMinutes")] public double? TypicalMinutes { get; set; }
    [DataMember(Name = "enabled")] public bool Enabled { get; set; }
}

/// <summary>POST /api/jobs success body.</summary>
[DataContract]
public sealed class JobReceipt
{
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "url")] public string Url { get; set; } = "";
    [DataMember(Name = "ahead")] public int Ahead { get; set; }
}

/// <summary>The form fields of a submission (the package file goes beside them).</summary>
public sealed record JobSubmission(string Project, string Plan, string Model, string Submitter, string Impact = "1");

/// <summary>The server answered with a status other than 200; <see cref="Body"/> is its text/plain reason (Korean).</summary>
public sealed class JobServerException : Exception
{
    public int Status { get; }
    public string Body { get; }
    public JobServerException(int status, string body) : base($"HTTP {status}: {body}") { Status = status; Body = body; }
}

/// <summary>Talks to ASH.Server's job API. Timeouts, connection errors and non-200 answers are thrown for the caller to catch.</summary>
public sealed class JobServerClient : IDisposable
{
    private readonly Uri _base;
    private readonly HttpClient _http;

    public JobServerClient(Uri server, HttpMessageHandler? handler = null)
    {
        _base = new Uri(server.AbsoluteUri.TrimEnd('/') + "/");
        _http = new HttpClient(handler ?? Update.UpdateClient.DefaultHandler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan }; // limits come from the tokens
    }

    public void Dispose() => _http.Dispose();

    public async Task<QueueInfo> GetQueueAsync(CancellationToken ct = default)
    {
        using var r = await _http.GetAsync(new Uri(_base, "api/jobs/queue"), ct).ConfigureAwait(false);
        return ParseQueue(await ReadOk(r).ConfigureAwait(false));
    }

    public async Task<JobReceipt> SubmitAsync(string zipPath, JobSubmission job, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        void Field(string name, string value) => form.Add(new StringContent(value, Encoding.UTF8), name);
        Field("project", job.Project); Field("plan", job.Plan); Field("model", job.Model); Field("submitter", job.Submitter); Field("impact", job.Impact);
        using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var file = new StreamContent(fs);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        form.Add(file, "package", "package.zip");
        using var r = await _http.PostAsync(new Uri(_base, "api/jobs"), form, ct).ConfigureAwait(false);
        return ParseReceipt(await ReadOk(r).ConfigureAwait(false));
    }

    private static async Task<string> ReadOk(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!r.IsSuccessStatusCode) throw new JobServerException((int)r.StatusCode, body.Trim());
        return body;
    }

    /// <exception cref="InvalidDataException">Not the expected JSON.</exception>
    public static QueueInfo ParseQueue(string json) => Parse<QueueInfo>(json);

    /// <exception cref="InvalidDataException">Not the expected JSON, or no id.</exception>
    public static JobReceipt ParseReceipt(string json)
    {
        var r = Parse<JobReceipt>(json);
        if (string.IsNullOrEmpty(r.Id)) throw new InvalidDataException("서버 응답에 작업 번호가 없습니다.");
        return r;
    }

    /// <summary>The job page as an absolute http/https address (a relative url is taken from the server's address); null when it is neither, so a reply can never make the
    /// plugin open something else (file:, a program path).</summary>
    public Uri? PageUri(JobReceipt r)
        => !string.IsNullOrWhiteSpace(r.Url) && Uri.TryCreate(_base, r.Url, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? u : null;

    private static T Parse<T>(string json) where T : class
    {
        try
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return new DataContractJsonSerializer(typeof(T)).ReadObject(ms) as T ?? throw new InvalidDataException("서버 응답이 비어 있습니다.");
        }
        catch (Exception e) when (e is SerializationException || e is ArgumentException || e is FormatException || e is InvalidCastException)
        {
            throw new InvalidDataException("서버 응답을 읽을 수 없습니다: " + e.Message, e);
        }
    }
}
