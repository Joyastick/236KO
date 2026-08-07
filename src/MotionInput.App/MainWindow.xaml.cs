using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MotionInput.Core.Engine;
using MotionInput.Core.HidHide;
using MotionInput.Core.Input;
using MotionInput.Core.Models;
using MotionInput.Core.Motion;
using MotionInput.Core.Output;
using MotionInput.Core.Profiles;

namespace MotionInput.App;

public partial class MainWindow : Window
{
    private readonly ControllerManager _controllerManager = new();
    private readonly ProfileStore _profileStore = new();
    private readonly HidHideService _hidHideService = new();
    private readonly DispatcherTimer _uiTimer;

    private readonly ObservableCollection<MotionRow> _motionRows = new();
    private readonly ObservableCollection<KeyValueRow> _attackBindingRows = new();
    private readonly ObservableCollection<MotionOutputRow> _motionOutputRows = new();
    private readonly ObservableCollection<KeyValueRow> _attackOutputRows = new();
    private readonly ObservableCollection<KeyValueRow> _keyOutputRows = new();
    private readonly ObservableCollection<HidHideDeviceRow> _hidHideDeviceRows = new();

    private Profile _profile = ProfileStore.CreateDefault();
    private MotionInputEngine? _engine;
    private IControllerSource? _controllerSource;
    private IVirtualGamepad? _gamepad;

    private volatile ControllerSnapshot? _pendingSnapshot;
    private volatile int _latestDirection = 5;

    public MainWindow()
    {
        InitializeComponent();

        MotionsGrid.ItemsSource = _motionRows;
        AttackBindingsGrid.ItemsSource = _attackBindingRows;
        MotionOutputsGrid.ItemsSource = _motionOutputRows;
        AttackOutputsGrid.ItemsSource = _attackOutputRows;
        KeyOutputsGrid.ItemsSource = _keyOutputRows;
        HidHideDevicesGrid.ItemsSource = _hidHideDeviceRows;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        LoadProfileList();
        RefreshControllers();
        RefreshHidHideStatus();
    }

    // ---------------- Controllers ----------------

    private void RefreshControllers()
    {
        var list = _controllerManager.ListAvailable();
        ControllerCombo.ItemsSource = list;
        if (list.Count > 0)
        {
            ControllerCombo.SelectedIndex = 0;
        }
        StatusText.Text = list.Count == 0 ? "No controllers detected." : $"{list.Count} controller(s) detected.";
    }

    private void RefreshControllersButton_Click(object sender, RoutedEventArgs e) => RefreshControllers();

    // ---------------- Profiles ----------------

    private void LoadProfileList()
    {
        var names = _profileStore.ListProfileNames();
        if (names.Count == 0)
        {
            _profileStore.Save(ProfileStore.CreateDefault());
            names = _profileStore.ListProfileNames();
        }

        ProfileCombo.ItemsSource = names;
        ProfileCombo.SelectedItem = names[0];
    }

    private void ProfileCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is string name && _profileStore.Exists(name))
        {
            _profile = _profileStore.Load(name);
            PopulateEditorFromProfile();
        }
    }

    private void PopulateEditorFromProfile()
    {
        UseDpadCheck.IsChecked = _profile.ControllerInput.DirectionSources.Contains("dpad");
        UseLeftStickCheck.IsChecked = _profile.ControllerInput.DirectionSources.Contains("left_stick");
        UseRightStickCheck.IsChecked = _profile.ControllerInput.DirectionSources.Contains("right_stick");
        StickDeadzoneBox.Text = _profile.ControllerInput.StickDeadzone.ToString(CultureInfo.InvariantCulture);
        TriggerThresholdBox.Text = _profile.ControllerInput.TriggerThreshold.ToString(CultureInfo.InvariantCulture);
        PollRateBox.Text = _profile.ControllerInput.PollRateHz.ToString(CultureInfo.InvariantCulture);

        MaxSequenceMsBox.Text = _profile.Leniency.MaxSequenceMs.ToString(CultureInfo.InvariantCulture);
        MaxGapMsBox.Text = _profile.Leniency.MaxGapMs.ToString(CultureInfo.InvariantCulture);
        AttackWindowMsBox.Text = _profile.Leniency.AttackWindowMs.ToString(CultureInfo.InvariantCulture);
        MotionCooldownMsBox.Text = _profile.Leniency.MotionCooldownMs.ToString(CultureInfo.InvariantCulture);

        _motionRows.Clear();
        foreach (var m in _profile.Motions)
        {
            _motionRows.Add(new MotionRow
            {
                Name = m.Name,
                SequenceText = string.Join(",", m.Sequence),
                AllowDiagonalSkip = m.AllowDiagonalSkip,
                MaxSequenceMsText = m.MaxSequenceMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                MaxGapMsText = m.MaxGapMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            });
        }

        _attackBindingRows.Clear();
        foreach (var (role, ids) in _profile.AttackBindings)
        {
            _attackBindingRows.Add(new KeyValueRow { Role = role, ValuesText = string.Join(",", ids) });
        }

        _motionOutputRows.Clear();
        foreach (var (motion, perRole) in _profile.MotionAttackOutputs)
        {
            foreach (var (role, tokens) in perRole)
            {
                _motionOutputRows.Add(new MotionOutputRow { Motion = motion, Role = role, TokensText = string.Join(",", tokens) });
            }
        }

        _attackOutputRows.Clear();
        foreach (var (role, tokens) in _profile.AttackOutputs)
        {
            _attackOutputRows.Add(new KeyValueRow { Role = role, ValuesText = string.Join(",", tokens) });
        }

        _keyOutputRows.Clear();
        foreach (var (id, tokens) in _profile.KeyOutputs)
        {
            _keyOutputRows.Add(new KeyValueRow { Role = id, ValuesText = string.Join(",", tokens) });
        }

        ApplicationPathBox.Text = _profile.HidHide.ApplicationPath ?? string.Empty;
    }

    private Profile BuildProfileFromEditor(string name)
    {
        CommitAllGridEdits();

        var profile = new Profile { Name = name };

        var sources = new List<string>();
        if (UseDpadCheck.IsChecked == true) sources.Add("dpad");
        if (UseLeftStickCheck.IsChecked == true) sources.Add("left_stick");
        if (UseRightStickCheck.IsChecked == true) sources.Add("right_stick");

        profile.ControllerInput = new ControllerInputSettings
        {
            DirectionSources = sources,
            StickDeadzone = ParseDouble(StickDeadzoneBox.Text, 0.35),
            TriggerThreshold = ParseDouble(TriggerThresholdBox.Text, 0.35),
            PollRateHz = ParseInt(PollRateBox.Text, 250),
            SelectedControllerId = (ControllerCombo.SelectedItem as ControllerDescriptor)?.Id,
        };

        profile.Leniency = new MotionLeniencySettings
        {
            MaxSequenceMs = ParseInt(MaxSequenceMsBox.Text, 500),
            MaxGapMs = ParseInt(MaxGapMsBox.Text, 250),
            AttackWindowMs = ParseInt(AttackWindowMsBox.Text, 300),
            MotionCooldownMs = ParseInt(MotionCooldownMsBox.Text, 150),
        };

        foreach (var row in _motionRows)
        {
            if (string.IsNullOrWhiteSpace(row.Name)) continue;
            var sequence = ParseIntList(row.SequenceText);
            if (sequence.Count == 0) continue;

            profile.Motions.Add(new MotionDefinition
            {
                Name = row.Name.Trim(),
                Sequence = sequence,
                AllowDiagonalSkip = row.AllowDiagonalSkip,
                MaxSequenceMs = string.IsNullOrWhiteSpace(row.MaxSequenceMsText) ? null : ParseInt(row.MaxSequenceMsText, 0),
                MaxGapMs = string.IsNullOrWhiteSpace(row.MaxGapMsText) ? null : ParseInt(row.MaxGapMsText, 0),
            });
        }

        foreach (var row in _attackBindingRows)
        {
            if (string.IsNullOrWhiteSpace(row.Role)) continue;
            profile.AttackBindings[row.Role.Trim()] = ParseStringList(row.ValuesText);
        }

        foreach (var row in _motionOutputRows)
        {
            if (string.IsNullOrWhiteSpace(row.Motion) || string.IsNullOrWhiteSpace(row.Role)) continue;
            if (!profile.MotionAttackOutputs.TryGetValue(row.Motion.Trim(), out var perRole))
            {
                perRole = new Dictionary<string, List<string>>();
                profile.MotionAttackOutputs[row.Motion.Trim()] = perRole;
            }
            perRole[row.Role.Trim()] = ParseStringList(row.TokensText);
        }

        foreach (var row in _attackOutputRows)
        {
            if (string.IsNullOrWhiteSpace(row.Role)) continue;
            profile.AttackOutputs[row.Role.Trim()] = ParseStringList(row.ValuesText);
        }

        foreach (var row in _keyOutputRows)
        {
            if (string.IsNullOrWhiteSpace(row.Role)) continue;
            profile.KeyOutputs[row.Role.Trim()] = ParseStringList(row.ValuesText);
        }

        profile.HidHide = new HidHideProfileSettings
        {
            ApplicationPath = string.IsNullOrWhiteSpace(ApplicationPathBox.Text) ? null : ApplicationPathBox.Text,
            CloakingEnabled = CloakingEnabledCheck.IsChecked == true,
            DeviceInstanceIds = _hidHideDeviceRows.Where(r => r.IsCloaked).Select(r => r.InstanceId).ToList(),
        };

        return profile;
    }

    private void CommitAllGridEdits()
    {
        foreach (var grid in new[] { MotionsGrid, AttackBindingsGrid, MotionOutputsGrid, AttackOutputsGrid, KeyOutputsGrid, HidHideDevicesGrid })
        {
            grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        }
    }

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static List<int> ParseIntList(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? (int?)v : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

    private static List<string> ParseStringList(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(ProfileCombo.Text) ? _profile.Name : ProfileCombo.Text.Trim();
        var profile = BuildProfileFromEditor(name);
        _profileStore.Save(profile);
        _profile = profile;

        var names = _profileStore.ListProfileNames();
        ProfileCombo.ItemsSource = names;
        ProfileCombo.SelectedItem = name;

        StatusText.Text = $"Saved profile '{name}'.";
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(ProfileCombo.Text) ? "New Profile" : ProfileCombo.Text.Trim();
        if (_profileStore.Exists(name))
        {
            MessageBox.Show($"A profile named '{name}' already exists. Type a different name in the profile box first.", "New profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = ProfileStore.CreateDefault(name);
        _profileStore.Save(profile);
        _profile = profile;

        var names = _profileStore.ListProfileNames();
        ProfileCombo.ItemsSource = names;
        ProfileCombo.SelectedItem = name;
        PopulateEditorFromProfile();

        StatusText.Text = $"Created profile '{name}' from the default template.";
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) return;

        var names = _profileStore.ListProfileNames();
        if (names.Count <= 1)
        {
            MessageBox.Show("At least one profile must remain.", "Delete profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Delete profile '{name}'?", "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _profileStore.Delete(name);
        LoadProfileList();
    }

    // ---------------- Engine ----------------

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is { IsRunning: true })
        {
            StopEngine();
        }
        else
        {
            StartEngine();
        }
    }

    private void StartEngine()
    {
        if (ControllerCombo.SelectedItem is not ControllerDescriptor descriptor)
        {
            MessageBox.Show("Select a controller first.", "No controller", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profile = BuildProfileFromEditor(_profile.Name);

        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            _controllerSource = _controllerManager.Create(descriptor, hwnd);
            _gamepad = new VirtualGamepad();
            _engine = new MotionInputEngine(_profile, _controllerSource, _gamepad);
            _engine.SnapshotPolled += OnSnapshotPolled;
            _engine.DirectionChanged += OnDirectionChanged;
            _engine.MotionDetected += OnMotionDetected;
            _engine.OutputFired += OnOutputFired;
            _engine.Start();

            StartStopButton.Content = "Stop";
            EngineStatusText.Text = "Running";
            EngineStatusDot.Fill = Brushes.LimeGreen;
            StatusText.Text = $"Engine started on {descriptor.DisplayName} with profile '{_profile.Name}'.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start: {ex.Message}\n\nMake sure the ViGEmBus driver is installed (https://github.com/ViGEm/ViGEmBus/releases).",
                "Start failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StopEngine();
        }
    }

    private void StopEngine()
    {
        if (_engine is not null)
        {
            _engine.SnapshotPolled -= OnSnapshotPolled;
            _engine.DirectionChanged -= OnDirectionChanged;
            _engine.MotionDetected -= OnMotionDetected;
            _engine.OutputFired -= OnOutputFired;
            _engine.Dispose();
            _engine = null;
        }

        _gamepad?.Dispose();
        _gamepad = null;
        _controllerSource?.Dispose();
        _controllerSource = null;

        StartStopButton.Content = "Start";
        EngineStatusText.Text = "Stopped";
        EngineStatusDot.Fill = Brushes.Gray;
        HeldInputsText.Text = "—";
        DirectionText.Text = "5";
        DirectionArrowText.Text = "neutral";
    }

    private void OnSnapshotPolled(ControllerSnapshot snapshot) => _pendingSnapshot = snapshot;

    private void OnDirectionChanged(int direction) => _latestDirection = direction;

    private void OnMotionDetected(MotionMatchResult match)
    {
        Dispatcher.Invoke(() =>
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss.fff}] Motion: {match.MotionName} ({match.StartDirection} -> {match.FinalDirection})");
            PendingMotionText.Text = $"{match.MotionName} — waiting up to {_profile.Leniency.AttackWindowMs}ms for an attack";
        });
    }

    private void OnOutputFired(string? motion, string? role)
    {
        Dispatcher.Invoke(() =>
        {
            var description = motion is null ? $"Attack: {role}" : $"{motion} + {role}";
            AppendLog($"[{DateTime.Now:HH:mm:ss.fff}] Output: {description}");
            LastOutputText.Text = description;
            PendingMotionText.Text = "—";
        });
    }

    private void AppendLog(string line)
    {
        MotionLogList.Items.Insert(0, line);
        while (MotionLogList.Items.Count > 200)
        {
            MotionLogList.Items.RemoveAt(MotionLogList.Items.Count - 1);
        }
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        var snapshot = _pendingSnapshot;
        if (snapshot is null) return;

        DirectionText.Text = _latestDirection.ToString(CultureInfo.InvariantCulture);
        DirectionArrowText.Text = DirectionName(_latestDirection);
        HeldInputsText.Text = snapshot.Digital.Count == 0 ? "—" : string.Join(", ", snapshot.Digital.OrderBy(x => x));
    }

    private static string DirectionName(int direction) => direction switch
    {
        8 => "up",
        2 => "down",
        4 => "left",
        6 => "right",
        7 => "up-left",
        9 => "up-right",
        1 => "down-left",
        3 => "down-right",
        _ => "neutral",
    };

    // ---------------- HidHide ----------------

    private void RefreshHidHideStatus()
    {
        HidHideStatusText.Text = _hidHideService.IsInstalled
            ? $"HidHide status: installed, {(_hidHideService.IsOperational ? "operational" : "driver not running")}"
            : "HidHide status: not installed. Install it from https://github.com/nefarius/HidHide/releases to hide the real controller.";

        CloakingEnabledCheck.IsChecked = _hidHideService.IsInstalled && _hidHideService.CloakingEnabled;
        RefreshHidHideDevices();
    }

    private void RefreshHidHideDevicesButton_Click(object sender, RoutedEventArgs e) => RefreshHidHideDevices();

    private void RefreshHidHideDevices()
    {
        _hidHideDeviceRows.Clear();
        if (!_hidHideService.IsInstalled) return;

        var blocked = _hidHideService.BlockedInstanceIds;
        foreach (var device in _hidHideService.ListDevices())
        {
            _hidHideDeviceRows.Add(new HidHideDeviceRow
            {
                InstanceId = device.InstanceId,
                FriendlyName = device.FriendlyName,
                IsCloaked = blocked.Contains(device.InstanceId, StringComparer.OrdinalIgnoreCase),
            });
        }
    }

    private void ApplyHidHideButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hidHideService.IsInstalled)
        {
            MessageBox.Show("HidHide is not installed.", "HidHide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CommitAllGridEdits();

        try
        {
            _hidHideService.CloakingEnabled = CloakingEnabledCheck.IsChecked == true;
            foreach (var row in _hidHideDeviceRows)
            {
                if (row.IsCloaked)
                {
                    _hidHideService.CloakDevice(row.InstanceId);
                }
                else
                {
                    _hidHideService.UncloakDevice(row.InstanceId);
                }
            }

            StatusText.Text = "HidHide settings applied. Unplug/replug the controller and relaunch the game for it to take effect.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to apply HidHide settings: {ex.Message}\n\nThis usually requires running 236KO as Administrator.",
                "HidHide error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Executable (*.exe)|*.exe", Title = "Select the game or launcher executable" };
        if (dialog.ShowDialog() == true)
        {
            ApplicationPathBox.Text = dialog.FileName;
        }
    }

    private void WhitelistSelfButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hidHideService.IsInstalled)
        {
            MessageBox.Show("HidHide is not installed.", "HidHide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _hidHideService.AllowSelf();
            StatusText.Text = "This app has been whitelisted so it can keep reading the real controller once cloaking is enabled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to whitelist this app: {ex.Message}\n\nThis usually requires running 236KO as Administrator.", "HidHide error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => StopEngine();
}
