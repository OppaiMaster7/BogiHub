using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using BogiHub.Core.Updates;

namespace BogiHub.Runtime;

/// <summary>
/// The opt-in "is there a newer BogiHub?" check: one GET of the repository's public release list from GitHub's API.
/// Nothing about the user, their settings or their controller is sent. Never runs unless the user turned it on in
/// Settings or pressed "Check now".
/// </summary>
public sealed class UpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/OppaiMaster7/BogiHub/releases?per_page=20";

    // Static initialisers run in this order: the client's User-Agent needs Current.
    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(0, 0, 0);
    private static readonly HttpClient Http = CreateClient();

    private readonly SemaphoreSlim _busy = new(1, 1);

    /// <summary>The newer release found by the last successful check, if any.</summary>
    public ReleaseEntry? Available { get; private set; }
    public DateTime? LastSuccessUtc { get; private set; }
    public string? LastError { get; private set; }
    public bool Checking => _busy.CurrentCount == 0;

    /// <summary>Asks GitHub once. Returns the newer release, or null (newest already, or the check failed: see LastError).</summary>
    public async Task<ReleaseEntry?> CheckAsync()
    {
        if (!await _busy.WaitAsync(0)) return Available; // one check at a time
        try
        {
            using var response = await Http.GetAsync(ReleasesApi);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(body);
            var releases = new List<ReleaseEntry>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.TryGetProperty("tag_name", out var tag) && r.TryGetProperty("html_url", out var url))
                    releases.Add(new ReleaseEntry(tag.GetString() ?? "", r.TryGetProperty("draft", out var d) && d.GetBoolean(), url.GetString() ?? ""));
            }
            Available = NewerRelease.Find(releases, Current);
            LastSuccessUtc = DateTime.UtcNow;
            LastError = null;
            return Available;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            LastError = ex is TaskCanceledException ? "GitHub did not answer in time" : ex.Message;
            return null;
        }
        finally
        {
            _busy.Release();
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub's API refuses requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BogiHub", Current.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }
}
