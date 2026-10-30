namespace BogiHub.Core.Updates;

/// <summary>One GitHub release as the update check sees it.</summary>
public readonly record struct ReleaseEntry(string Tag, bool Draft, string Url);

/// <summary>Picks the release to offer from the repository's release list. Pure, so it is unit tested.</summary>
public static class NewerRelease
{
    /// <summary>The only page an update notice may open: this repository's releases.</summary>
    public const string ReleasesPrefix = "https://github.com/OppaiMaster7/BogiHub/releases/";

    /// <summary>"v0.1.2" or "0.1.2" → 0.1.2. Anything else ("v0.2.0-beta", "nightly") → null, and is skipped.</summary>
    public static Version? ParseTag(string tag)
    {
        string t = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        return t.Split('.').Length == 3 && Version.TryParse(t, out var v) ? v : null;
    }

    /// <summary>
    /// The highest release newer than <paramref name="current"/>, or null. Pre-releases count (every BogiHub release
    /// so far is one); drafts and releases whose page is not on this repository never do.
    /// </summary>
    public static ReleaseEntry? Find(IEnumerable<ReleaseEntry> releases, Version current)
    {
        var mine = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        ReleaseEntry? best = null;
        Version? bestVersion = null;
        foreach (var r in releases)
        {
            if (r.Draft || !r.Url.StartsWith(ReleasesPrefix, StringComparison.Ordinal)) continue;
            if (ParseTag(r.Tag) is not { } v || v <= mine) continue;
            if (bestVersion is null || v > bestVersion) (best, bestVersion) = (r, v);
        }
        return best;
    }
}
