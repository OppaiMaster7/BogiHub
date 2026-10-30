using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BogiHub.App.Theme;
using BogiHub.Core.Profiles;
using BogiHub.Runtime;

namespace BogiHub.App.Pages;

public sealed class ProfilesPage : UserControl, IPage
{
    private readonly HubRuntime _runtime;
    private readonly MainWindow _window;
    private readonly StackPanel _list = new() { Spacing = 14 };
    private readonly TextBlock _message = Ui.Text("", "muted");
    private readonly List<Button> _addFocusedButtons = [];
    private string? _lastGameExe;
    private string? _renamingId;
    private string? _activeId;

    private static readonly (string Name, string Exe)[] KnownGames =
    [
        ("Fortnite", "FortniteClient-Win64-Shipping.exe"),
    ];

    public ProfilesPage(HubRuntime runtime, MainWindow window)
    {
        _runtime = runtime;
        _window = window;

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Ui.Stack(4,
            Ui.Text("Profiles", "h1"),
            Ui.Text("A profile switches on automatically when one of its games is focused. Pin a profile to use it everywhere.", "muted")));
        var actions = Ui.Row(8, Ui.Button("Import…", Import, "ghost"), Ui.Button("New profile", NewProfile, "primary"));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        root.Children.Add(header);
        _message.Margin = new Thickness(0, 8);
        Grid.SetRow(_message, 1);
        root.Children.Add(_message);
        var scroll = new ScrollViewer { Content = _list, Padding = new Thickness(0, 0, 6, 0) };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Content = root;

        runtime.ProfilesChanged += () => Dispatcher.UIThread.Post(Rebuild);
        Rebuild();
    }

    public void Refresh(RuntimeStatus s)
    {
        var fg = s.Foreground;
        if (!fg.IsSelf && !fg.IsDesktopShell && fg.ExeName is not null && !fg.ExeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            _lastGameExe = fg.ExeName;
        foreach (var b in _addFocusedButtons)
        {
            b.IsVisible = _lastGameExe is not null;
            b.Content = $"Add {_lastGameExe}";
        }
        if (s.Decision.Profile?.Id != _activeId)
        {
            _activeId = s.Decision.Profile?.Id;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        _addFocusedButtons.Clear();
        foreach (var p in _runtime.Profiles) _list.Children.Add(Card(p));
    }

    private Control Card(Profile p)
    {
        var s = _runtime.Settings;
        bool builtIn = DefaultProfiles.ById(p.Id) is not null;
        bool isDefault = s.DefaultProfileId == p.Id;
        bool pinned = s.PinnedProfileId == p.Id;

        // Title row: name (or rename box) + badges
        Control title;
        if (_renamingId == p.Id)
        {
            var box = new TextBox { Text = p.Name, Width = 240, MaxLength = ProfileValidator.MaxNameLength };
            var ok = Ui.Button("Save name", () =>
            {
                if (string.IsNullOrWhiteSpace(box.Text)) return;
                var copy = p.Clone();
                copy.Name = box.Text.Trim();
                _renamingId = null;
                Save(copy);
            }, "primary");
            var cancel = Ui.Button("Cancel", () => { _renamingId = null; Rebuild(); }, "ghost");
            title = Ui.Row(8, box, ok, cancel);
        }
        else
        {
            var row = Ui.Row(8, Ui.Text(p.Name, "h2"));
            row.Children.Add(Badge(p.Output switch
            {
                OutputMode.KeyboardMouse => "Keyboard & mouse",
                OutputMode.Hybrid => "Hybrid",
                _ => "Xbox controller",
            }, Palette.Muted));
            if (p.Id == _activeId) row.Children.Add(Badge("Active now", Palette.Accent));
            if (isDefault) row.Children.Add(Badge("Default", Palette.Ok));
            if (pinned) row.Children.Add(Badge("Pinned", Palette.Warn));
            if (builtIn) row.Children.Add(Badge("Built-in", Palette.Faint));
            title = row;
        }

        // Games
        var games = new WrapPanel();
        foreach (string exe in p.Executables)
        {
            var remove = new Button { Content = "×", Padding = new Thickness(6, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Palette.MutedBrush };
            string e = exe;
            remove.Click += (_, _) =>
            {
                var copy = p.Clone();
                copy.Executables.Remove(e);
                Save(copy);
            };
            games.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(99),
                Background = Palette.Surface2Brush,
                BorderBrush = Palette.LineBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 2, 2, 2),
                Margin = new Thickness(0, 0, 6, 6),
                Child = Ui.Row(2, new TextBlock { Text = exe, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, remove),
            });
        }
        if (p.Executables.Count == 0)
            games.Children.Add(Ui.Text(isDefault ? "No games listed — used for fullscreen games that have no profile of their own." : "No games yet.", "muted"));

        var exeBox = new TextBox { PlaceholderText = "game.exe", Width = 220 };
        void AddExe(string? exe)
        {
            exe = exe?.Trim();
            if (string.IsNullOrEmpty(exe)) return;
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            if (p.Executables.Contains(exe, StringComparer.OrdinalIgnoreCase)) return;
            var owner = _runtime.Profiles.FirstOrDefault(x => x.Id != p.Id && x.Executables.Contains(exe, StringComparer.OrdinalIgnoreCase));
            var copy = p.Clone();
            copy.Executables.Add(exe);
            Save(copy, owner is null ? null : $"Note: {exe} is also listed in \"{owner.Name}\". The first matching profile in the list wins.");
        }
        var addFocused = Ui.Button("", () => AddExe(_lastGameExe), "ghost");
        addFocused.IsVisible = false;
        _addFocusedButtons.Add(addFocused);
        var addRow = new WrapPanel { Children = { exeBox, Ui.Button("Add game", () => AddExe(exeBox.Text)), addFocused } };
        foreach (var (name, exe) in KnownGames)
            if (!p.Executables.Contains(exe, StringComparer.OrdinalIgnoreCase))
                addRow.Children.Add(Ui.Button($"+ {name}", () => AddExe(exe), "ghost"));
        foreach (var c in addRow.Children) c.Margin = new Thickness(0, 0, 8, 0);

        // Actions
        var actions = new WrapPanel();
        void A(Control c)
        {
            c.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(c);
        }
        A(Ui.Button("Edit layout", () => _window.Navigate("controls", page => ((ControlsPage)page).Edit(p.Id))));
        if (!isDefault) A(Ui.Button("Set as default", () => { _runtime.UpdateSettings(x => x.DefaultProfileId = p.Id); Rebuild(); }, "ghost"));
        A(Ui.Button(pinned ? "Unpin" : "Pin (use everywhere)", () => { _runtime.UpdateSettings(x => x.PinnedProfileId = pinned ? null : p.Id); Rebuild(); }, "ghost"));
        A(Ui.Button("Rename", () => { _renamingId = p.Id; Rebuild(); }, "ghost"));
        A(Ui.Button("Duplicate", () => Duplicate(p), "ghost"));
        A(Ui.Button("Export…", () => Export(p), "ghost"));
        A(Ui.Button(builtIn ? "Reset to original" : "Delete", () => Delete(p, builtIn), "ghost"));

        var body = Ui.Stack(10, title);
        if (!string.IsNullOrWhiteSpace(p.Description)) body.Children.Add(Ui.Text(p.Description!, "muted"));
        body.Children.Add(Ui.Text("GAMES", "eyebrow"));
        body.Children.Add(games);
        body.Children.Add(addRow);
        body.Children.Add(Ui.Divider());
        body.Children.Add(actions);

        var card = Ui.Card(body);
        card.Highlight = p.Id == _activeId;
        return card;
    }

    private static Border Badge(string text, Color color) => new()
    {
        CornerRadius = new CornerRadius(99),
        Padding = new Thickness(8, 2),
        Background = Palette.Brush(color, 0.12),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = Palette.Brush(color) },
    };

    private void Save(Profile p, string? note = null)
    {
        try
        {
            _runtime.SaveProfile(p);
            _message.Text = note ?? "";
        }
        catch (Exception ex)
        {
            _message.Text = $"Could not save: {ex.Message}";
        }
        Rebuild();
    }

    private void NewProfile()
    {
        var p = DefaultProfiles.Controller();
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = UniqueName("New profile");
        p.Description = null;
        Save(p);
        _renamingId = p.Id;
        Rebuild();
    }

    private void Duplicate(Profile src)
    {
        var p = src.Clone();
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = UniqueName(src.Name + " copy");
        p.Executables.Clear(); // two profiles for the same game would be ambiguous
        Save(p);
    }

    private string UniqueName(string baseName)
    {
        var names = _runtime.Profiles.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string name = baseName;
        for (int i = 2; names.Contains(name); i++) name = $"{baseName} {i}";
        return name.Length > ProfileValidator.MaxNameLength ? name[..ProfileValidator.MaxNameLength] : name;
    }

    private void Delete(Profile p, bool builtIn)
    {
        _runtime.DeleteProfile(p.Id);
        _message.Text = builtIn ? $"\"{p.Name}\" was reset to its original layout." : $"Deleted \"{p.Name}\".";
        Rebuild();
    }

    private async void Export(Profile p)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export profile",
            SuggestedFileName = string.Concat(p.Name.Split(Path.GetInvalidFileNameChars())) + ".bogihub.json",
            FileTypeChoices = [new FilePickerFileType("BogiHub profile") { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        ProfileStore.Export(p, path);
        _message.Text = $"Exported to {path}";
    }

    private async void Import()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import profile",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("BogiHub profile") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        var (profile, errors) = _runtime.ImportProfile(path);
        _message.Text = profile is not null ? $"Imported \"{profile.Name}\"." : "Import rejected: " + string.Join(" ", errors);
        Rebuild();
    }
}
