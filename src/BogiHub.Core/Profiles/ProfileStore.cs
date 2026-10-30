using System.Text.Json;

namespace BogiHub.Core.Profiles;

public sealed record ProfileLoadError(string FilePath, string Message);

/// <summary>
/// Loads and saves profiles as one JSON file per profile in a folder (normally %APPDATA%\BogiHub\profiles).
/// Saves are atomic: write a temp file, flush, then rename over the old file, so a crash never leaves half a profile.
/// </summary>
public sealed class ProfileStore(string directory)
{
    public const int MaxFileBytes = 256 * 1024;

    public string Directory { get; } = directory;

    public (List<Profile> Profiles, List<ProfileLoadError> Errors) LoadAll()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var profiles = new List<Profile>();
        var errors = new List<ProfileLoadError>();
        var seenIds = new HashSet<string>();

        foreach (string file in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order())
        {
            var (profile, problems) = TryLoad(file);
            if (profile is null)
            {
                errors.Add(new ProfileLoadError(file, string.Join(" ", problems)));
                continue;
            }
            if (!seenIds.Add(profile.Id))
            {
                errors.Add(new ProfileLoadError(file, $"Duplicate profile id \"{profile.Id}\"; skipped."));
                continue;
            }
            profiles.Add(profile);
        }

        // Built-in presets are always available, listed first in their usual order. A saved copy with the same id (user edits) wins.
        profiles.InsertRange(0, DefaultProfiles.All().Where(b => seenIds.Add(b.Id)));

        return (profiles, errors);
    }

    /// <summary>Reads and validates one file. Returns null with reasons if it cannot be used.</summary>
    public static (Profile? Profile, List<string> Errors) TryLoad(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes) return (null, [$"File is larger than {MaxFileBytes / 1024} KB."]);
            var profile = ProfileJson.Deserialize(File.ReadAllText(path));
            var errors = ProfileValidator.Validate(profile);
            return errors.Count == 0 ? (profile, errors) : (null, errors);
        }
        catch (JsonException ex)
        {
            return (null, [$"Not a valid profile file: {ex.Message}"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, [$"Could not read file: {ex.Message}"]);
        }
    }

    public void Save(Profile profile)
    {
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0) throw new InvalidOperationException("Refusing to save an invalid profile: " + string.Join(" ", errors));

        System.IO.Directory.CreateDirectory(Directory);
        string target = PathFor(profile.Id);
        string temp = target + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(ProfileJson.Serialize(profile));
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, target, overwrite: true);
    }

    public void Delete(string id)
    {
        string path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Imports a profile file from anywhere. Gives it a fresh id if it would overwrite an existing one.</summary>
    public (Profile? Profile, List<string> Errors) Import(string path, IEnumerable<string> existingIds)
    {
        var (profile, errors) = TryLoad(path);
        if (profile is null) return (null, errors);
        if (existingIds.Contains(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
        Save(profile);
        return (profile, errors);
    }

    public static void Export(Profile profile, string path) => File.WriteAllText(path, ProfileJson.Serialize(profile));

    private string PathFor(string id) => Path.Combine(Directory, id + ".json");
}
