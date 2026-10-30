using BogiHub.Core.Input;
using BogiHub.Core.Output;
using BogiHub.Core.Profiles;

namespace BogiHub.Core.Tests;

public sealed class ProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bogihub-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        foreach (var p in DefaultProfiles.All()) Assert.Empty(ProfileValidator.Validate(p));
    }

    [Fact]
    public void Json_round_trip_is_lossless_and_readable()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.Executables.Add("game.exe");
        string json = ProfileJson.Serialize(p);

        Assert.Contains("\"A\": \"key:Space\"", json);
        Assert.Contains("\"mode\": \"Keys\"", json);
        Assert.Equal(json, ProfileJson.Serialize(ProfileJson.Deserialize(json)));
    }

    [Theory]
    [InlineData("gamepad:A", BindingKind.Gamepad)]
    [InlineData("key:space", BindingKind.Key)]
    [InlineData("mouse:WheelUp", BindingKind.Mouse)]
    [InlineData("none", BindingKind.None)]
    public void Binding_parses(string text, BindingKind kind)
    {
        Assert.True(Binding.TryParse(text, out var b));
        Assert.Equal(kind, b!.Value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gamepad:Z")]
    [InlineData("key:65")]
    [InlineData("script:rm -rf")]
    [InlineData("A")]
    public void Binding_rejects_garbage(string text) => Assert.False(Binding.TryParse(text, out _));

    [Fact]
    public void Save_then_load_and_builtins_are_always_present()
    {
        var store = new ProfileStore(_dir);
        var custom = DefaultProfiles.Controller();
        custom.Id = "my-fortnite";
        custom.Name = "Fortnite";
        custom.Executables.Add("FortniteClient-Win64-Shipping.exe");
        store.Save(custom);

        var (profiles, errors) = store.LoadAll();
        Assert.Empty(errors);
        Assert.Contains(profiles, p => p.Id == "my-fortnite" && p.Executables.Single() == "FortniteClient-Win64-Shipping.exe");
        Assert.Contains(profiles, p => p.Id == DefaultProfiles.ControllerId);
        Assert.Contains(profiles, p => p.Id == DefaultProfiles.KeyboardMouseId);
        Assert.False(File.Exists(Path.Combine(_dir, "my-fortnite.json.tmp")));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("{\"schemaVersion\": 99, \"id\": \"x\", \"name\": \"x\"}")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"x\", \"name\": \"x\", \"buttons\": {\"A\": \"script:format c:\"}}")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"x\", \"name\": \"x\", \"leftStick\": {\"deadzone\": 5}}")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"../../evil\", \"name\": \"x\"}")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"x\", \"name\": \"x\", \"executables\": [\"C:\\\\bad\\\\path.exe\"]}")]
    [InlineData("null")]
    public void Corrupted_or_hostile_import_is_rejected_and_nothing_is_written(string json)
    {
        Directory.CreateDirectory(_dir);
        string src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(src, json);
        try
        {
            var store = new ProfileStore(Path.Combine(_dir, "profiles"));
            var (profile, errors) = store.Import(src, []);
            Assert.Null(profile);
            Assert.NotEmpty(errors);
            Assert.False(Directory.Exists(store.Directory) && Directory.EnumerateFiles(store.Directory).Any());
        }
        finally
        {
            File.Delete(src);
        }
    }

    [Fact]
    public void Corrupted_file_in_folder_is_reported_but_does_not_block_others()
    {
        var store = new ProfileStore(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{{{");
        var (profiles, errors) = store.LoadAll();
        Assert.Single(errors);
        Assert.Equal(2, profiles.Count);
    }

    [Fact]
    public void Import_with_clashing_id_gets_new_id()
    {
        string src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        ProfileStore.Export(DefaultProfiles.Controller(), src);
        try
        {
            var (profile, _) = new ProfileStore(_dir).Import(src, [DefaultProfiles.ControllerId]);
            Assert.NotNull(profile);
            Assert.NotEqual(DefaultProfiles.ControllerId, profile!.Id);
        }
        finally
        {
            File.Delete(src);
        }
    }

    [Fact]
    public void Keyboard_profile_with_gamepad_binding_is_flagged()
    {
        var p = DefaultProfiles.KeyboardMouse();
        p.Buttons[PhysicalInput.A] = Binding.Pad(GamepadButton.A);
        Assert.NotEmpty(ProfileValidator.Validate(p));
    }

    [Fact]
    public void Key_enum_values_are_virtual_key_codes()
    {
        Assert.Equal(0x41, (int)Key.A);
        Assert.Equal(0x5A, (int)Key.Z);
        Assert.Equal(0x39, (int)Key.D9);
        Assert.Equal(0x7B, (int)Key.F12);
    }
}
