using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MotionInput.Core.Cloak;
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
    private readonly DispatcherTimer _uiTimer;

    private readonly ObservableCollection<MotionRow> _motionRows = new();
    private readonly ObservableCollection<KeyValueRow> _attackBindingRows = new();
    private readonly ObservableCollection<MotionOutputRow> _motionOutputRows = new();
    private readonly ObservableCollection<KeyValueRow> _attackOutputRows = new();
    private readonly ObservableCollection<KeyValueRow> _keyOutputRows = new();
    private readonly ObservableCollection<BindingRow> _bindingRows = new();
    private readonly ProcessCloakService _cloakService = new();
    private readonly HidHideService _hidHideService = new();
    private readonly ObservableCollection<HidHideDeviceRow> _hidHideDeviceRows = new();

    private Profile _profile = ProfileStore.CreateDefault();
    private MotionInputEngine? _engine;
    private IControllerSource? _controllerSource;
    private IVirtualGamepad? _gamepad;
    private CancellationTokenSource? _captureCts;

    private volatile ControllerSnapshot? _pendingSnapshot;
    private volatile int _latestDirection = 5;

    /// <summary>Role names offered by the Motion + Attack Outputs role dropdowns (same vocabulary as the Bindings tab).</summary>
    public IReadOnlyList<string> RoleNameOptions { get; } = ButtonRoles.Names;

    /// <summary>Virtual Xbox 360 buttons offered by the Bindings tab's per-role Output dropdown.</summary>
    public IReadOnlyList<string> XInputButtonOptions { get; } = XInputButtons.Names;

    /// <summary>Direction choices offered by the Motion + Attack Outputs direction dropdown.</summary>
    public IReadOnlyList<DirectionOutputOption> DirectionOutputOptions { get; } = new[]
    {
        new DirectionOutputOption("(none)", ""),
        new DirectionOutputOption("1 – Down-Left", "1"),
        new DirectionOutputOption("2 – Down", "2"),
        new DirectionOutputOption("3 – Down-Right", "3"),
        new DirectionOutputOption("4 – Left", "4"),
        new DirectionOutputOption("5 – Neutral", "5"),
        new DirectionOutputOption("6 – Right", "6"),
        new DirectionOutputOption("7 – Up-Left", "7"),
        new DirectionOutputOption("8 – Up", "8"),
        new DirectionOutputOption("9 – Up-Right", "9"),
        new DirectionOutputOption("Motion's Final Direction", "$controller_motion_final"),
        new DirectionOutputOption("Motion's Start Direction", "$controller_motion_start"),
    };

    private static readonly HashSet<string> DirectionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "$controller_motion_final", "$controller_motion_start",
    };

    public MainWindow()
    {
        InitializeComponent();

        MotionsGrid.ItemsSource = _motionRows;
        MotionOutputsGrid.ItemsSource = _motionOutputRows;
        AttackOutputsGrid.ItemsSource = _attackOutputRows;

        _bindingRows.Add(new BindingRow { Role = "left", DisplayName = "Left", IsDirection = true });
        _bindingRows.Add(new BindingRow { Role = "right", DisplayName = "Right", IsDirection = true });
        _bindingRows.Add(new BindingRow { Role = "up", DisplayName = "Up", IsDirection = true });
        _bindingRows.Add(new BindingRow { Role = "down", DisplayName = "Down", IsDirection = true });
        foreach (var role in ButtonRoles.Names)
        {
            _bindingRows.Add(new BindingRow { Role = role, DisplayName = char.ToUpperInvariant(role[0]) + role[1..], IsDirection = false });
        }
        BindingRowsControl.ItemsSource = _bindingRows;
        HidHideDevicesControl.ItemsSource = _hidHideDeviceRows;

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
                var direction = tokens.FirstOrDefault(t => DirectionTokens.Contains(t)) ?? "";
                var outputRoles = tokens.Where(t => !DirectionTokens.Contains(t)).ToList();
                _motionOutputRows.Add(new MotionOutputRow
                {
                    Motion = motion,
                    Role = role,
                    Direction = direction,
                    OutputRole = outputRoles.ElementAtOrDefault(0) ?? "",
                    OutputRole2 = outputRoles.ElementAtOrDefault(1) ?? "",
                });
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

        RefreshBindingRowsDisplay();
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

            var tokens = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Direction)) tokens.Add(row.Direction.Trim());
            if (!string.IsNullOrWhiteSpace(row.OutputRole)) tokens.Add(row.OutputRole.Trim());
            if (!string.IsNullOrWhiteSpace(row.OutputRole2)) tokens.Add(row.OutputRole2.Trim());
            if (tokens.Count == 0) continue;

            if (!profile.MotionAttackOutputs.TryGetValue(row.Motion.Trim(), out var perRole))
            {
                perRole = new Dictionary<string, List<string>>();
                profile.MotionAttackOutputs[row.Motion.Trim()] = perRole;
            }
            perRole[row.Role.Trim()] = tokens;
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

        return profile;
    }

    private void CommitAllGridEdits()
    {
        foreach (var grid in new[] { MotionsGrid, MotionOutputsGrid, AttackOutputsGrid })
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
        AnalogValuesText.Text = "—";
        DirectionText.Text = "5";
        DirectionArrowText.Text = "neutral";
    }

    private void OnSnapshotPolled(ControllerSnapshot snapshot) => _pendingSnapshot = snapshot;

    private void OnDirectionChanged(int direction) => _latestDirection = direction;

    private void OnMotionDetected(MotionMatchResult match)
    {
        Dispatcher.Invoke(() =>
        {
            var motionDef = _profile.Motions.FirstOrDefault(m => m.Name == match.MotionName);
            var sequenceText = motionDef is not null ? string.Join(",", motionDef.Sequence) : $"{match.StartDirection}->{match.FinalDirection}";
            AppendLog($"[{DateTime.Now:HH:mm:ss.fff}] Motion: {match.MotionName} ({sequenceText})");
            PendingMotionText.Text = $"{match.MotionName} — waiting up to {_profile.Leniency.AttackWindowMs}ms for an attack";
        });
    }

    private void OnOutputFired(string? motion, string? role, IReadOnlyList<string> tokens)
    {
        Dispatcher.Invoke(() =>
        {
            var outputText = string.Join(", ", tokens);
            var trigger = motion is null ? $"Attack: {role}" : $"{motion} + {role}";
            AppendLog($"[{DateTime.Now:HH:mm:ss.fff}] Output: {trigger} -> {outputText}");
            LastOutputText.Text = outputText;
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
        AnalogValuesText.Text = snapshot.Analog.Count == 0
            ? "—"
            : string.Join(Environment.NewLine, snapshot.Analog.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {kv.Value:F2}"));
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

    // ---------------- Bindings ----------------

    private void DirectionSourceCheck_Changed(object sender, RoutedEventArgs e) => RefreshBindingRowsDisplay();

    // ---------------- HidHide Cloak ----------------

    private void RefreshHidHideButton_Click(object sender, RoutedEventArgs e) => RefreshHidHideStatus();

    private void RefreshHidHideStatus()
    {
        if (!_hidHideService.IsInstalled)
        {
            HidHideStatusText.Text = "HidHide status: not installed. Install it from https://github.com/nefarius/HidHide/releases, then click Refresh again.";
            _hidHideDeviceRows.Clear();
            HidHideDevicesEmptyText.Visibility = Visibility.Visible;
            return;
        }

        HidHideStatusText.Text = $"HidHide status: installed, {(_hidHideService.IsOperational ? "operational" : "driver not running")}";
        HidHideCloakingEnabledCheck.IsChecked = _hidHideService.CloakingEnabled;

        var blocked = _hidHideService.BlockedInstanceIds;
        _hidHideDeviceRows.Clear();
        foreach (var device in _hidHideService.ListDevices())
        {
            _hidHideDeviceRows.Add(new HidHideDeviceRow
            {
                InstanceId = device.InstanceId,
                FriendlyName = device.FriendlyName,
                IsCloaked = blocked.Contains(device.InstanceId, StringComparer.OrdinalIgnoreCase),
            });
        }

        HidHideDevicesEmptyText.Visibility = _hidHideDeviceRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WhitelistSelfHidHideButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hidHideService.IsInstalled)
        {
            MessageBox.Show("HidHide is not installed.", "HidHide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _hidHideService.AllowSelf();
            StatusText.Text = "236KO whitelisted with HidHide — it'll keep reading cloaked devices even once cloaking is enabled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to whitelist this app: {ex.Message}\n\nThis usually requires running 236KO as Administrator.", "HidHide error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HidHideCloakingEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_hidHideService.IsInstalled) return;

        try
        {
            _hidHideService.CloakingEnabled = HidHideCloakingEnabledCheck.IsChecked == true;
            StatusText.Text = $"HidHide cloaking {(_hidHideService.CloakingEnabled ? "enabled" : "disabled")}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to toggle cloaking: {ex.Message}\n\nThis usually requires running 236KO as Administrator.", "HidHide error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HidHideDeviceCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { Tag: HidHideDeviceRow row } || !_hidHideService.IsInstalled) return;

        try
        {
            if (row.IsCloaked)
            {
                _hidHideService.CloakDevice(row.InstanceId);
            }
            else
            {
                _hidHideService.UncloakDevice(row.InstanceId);
            }
            StatusText.Text = $"{(row.IsCloaked ? "Cloaked" : "Uncloaked")} \"{row.FriendlyName}\". Unplug/replug it (or relaunch the game) for the change to take effect.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update cloaking for this device: {ex.Message}\n\nThis usually requires running 236KO as Administrator.", "HidHide error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------- Hide from Game (experimental) ----------------

    private async void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: BindingRow row }) return;

        if (row.IsListening)
        {
            _captureCts?.Cancel();
            return;
        }

        if (_captureCts is not null)
        {
            return; // one capture at a time
        }

        if (ControllerCombo.SelectedItem is not ControllerDescriptor descriptor)
        {
            MessageBox.Show("Select a controller first.", "No controller", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        row.IsListening = true;
        StatusText.Text = row.IsDirection
            ? $"Push {row.DisplayName} on the D-Pad or a stick…"
            : $"Press the button for {row.DisplayName}…";

        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;
        IControllerSource? source = null;

        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            source = _controllerManager.Create(descriptor, hwnd);

            var detected = await Task.Run(() => CaptureInput(source, row, token), token);

            if (detected is null)
            {
                StatusText.Text = $"No input detected for {row.DisplayName}.";
            }
            else
            {
                ApplyCapture(row, detected);
                StatusText.Text = $"Bound {row.DisplayName} to {detected}.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Binding cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to read the controller: {ex.Message}", "Binding error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            source?.Dispose();
            row.IsListening = false;
            _captureCts?.Dispose();
            _captureCts = null;
        }
    }

    /// <summary>Runs on a background thread: polls until the expected input changes, times out, or is cancelled.</summary>
    private static string? CaptureInput(IControllerSource source, BindingRow row, CancellationToken token)
    {
        var previous = source.Poll();
        var deadline = DateTime.UtcNow.AddSeconds(8);

        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            Thread.Sleep(8);
            var current = source.Poll();

            var detected = row.IsDirection
                ? InputCapture.DetectDirectionSource(previous, current, row.Role)
                : InputCapture.DetectNewButtonPress(previous, current);

            if (detected is not null)
            {
                return detected;
            }

            previous = current;
        }

        return null;
    }

    private void ApplyCapture(BindingRow row, string detected)
    {
        if (row.IsDirection)
        {
            switch (detected)
            {
                case "dpad": UseDpadCheck.IsChecked = true; break;
                case "left_stick": UseLeftStickCheck.IsChecked = true; break;
                case "right_stick": UseRightStickCheck.IsChecked = true; break;
            }
        }
        else
        {
            // Every button-style role — Light/Medium/Heavy included — is keyed by its role name in
            // AttackBindings/AttackOutputs, so it round-trips through a saved profile and can be
            // named directly in a combo's output tokens (e.g. "S1").
            SetRowValue(_attackBindingRows, row.Role, detected);

            // The physical input and the virtual output are separate concerns: a DirectInput
            // controller's "button5" isn't a valid Xbox button name, so it can't just be passed
            // through the way an XInput controller's own button names conveniently can. Re-capturing
            // *what triggers* this role (e.g. re-binding it for a different controller) must not
            // clobber an already-configured, still-valid Output — only fill in a default when there
            // wasn't one there yet.
            if (string.IsNullOrEmpty(row.OutputButton) &&
                XInputButtonOptions.Any(b => string.Equals(b, detected, StringComparison.OrdinalIgnoreCase)))
            {
                row.OutputButton = detected.ToLowerInvariant();
                SetRowValue(_attackOutputRows, row.Role, $"controller:{row.OutputButton}");
            }

            AttackOutputsGrid.Items.Refresh();
        }

        RefreshBindingRowsDisplay();
    }

    private void OutputButtonCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox { Tag: BindingRow row } || row.IsDirection) return;

        if (string.IsNullOrEmpty(row.OutputButton))
        {
            RemoveRowValue(_attackOutputRows, row.Role);
        }
        else
        {
            SetRowValue(_attackOutputRows, row.Role, $"controller:{row.OutputButton}");
        }

        AttackOutputsGrid.Items.Refresh();
    }

    private static void SetRowValue(ObservableCollection<KeyValueRow> rows, string role, string valuesText)
    {
        var existing = rows.FirstOrDefault(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            rows.Add(new KeyValueRow { Role = role, ValuesText = valuesText });
        }
        else
        {
            existing.ValuesText = valuesText;
        }
    }

    private static void RemoveRowValue(ObservableCollection<KeyValueRow> rows, string role)
    {
        var existing = rows.FirstOrDefault(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) rows.Remove(existing);
    }

    private void RefreshBindingRowsDisplay()
    {
        var sources = new List<string>();
        if (UseDpadCheck.IsChecked == true) sources.Add("D-Pad");
        if (UseLeftStickCheck.IsChecked == true) sources.Add("Left Stick");
        if (UseRightStickCheck.IsChecked == true) sources.Add("Right Stick");
        var directionSummary = sources.Count == 0 ? "Not bound" : string.Join(", ", sources);

        foreach (var row in _bindingRows)
        {
            if (row.IsDirection)
            {
                row.BoundText = directionSummary;
                continue;
            }

            var match = _attackBindingRows.FirstOrDefault(r => string.Equals(r.Role, row.Role, StringComparison.OrdinalIgnoreCase));
            row.BoundText = match is not null && !string.IsNullOrWhiteSpace(match.ValuesText) ? match.ValuesText : "Not bound";

            // Sync the Output dropdown from whatever AttackOutputs currently holds (e.g. after
            // loading a saved profile), so it doesn't silently reset to blank on every refresh.
            var outputMatch = _attackOutputRows.FirstOrDefault(r => string.Equals(r.Role, row.Role, StringComparison.OrdinalIgnoreCase));
            var outputToken = outputMatch?.ValuesText.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault(t => t.StartsWith("controller:", StringComparison.OrdinalIgnoreCase));
            row.OutputButton = outputToken is not null ? outputToken["controller:".Length..].ToLowerInvariant() : string.Empty;
        }
    }

    // ---------------- Per-Process Cloak ----------------

    private void RefreshCloakTargetsButton_Click(object sender, RoutedEventArgs e)
    {
        var xinputControllers = _controllerManager.ListAvailable().Where(c => c.Backend == ControllerBackend.XInput).ToList();
        CloakControllerCombo.ItemsSource = xinputControllers;
        if (xinputControllers.Count > 0) CloakControllerCombo.SelectedIndex = 0;

        var processCount = RefreshCloakProcessList();

        StatusText.Text = $"Cloak targets refreshed: {xinputControllers.Count} XInput controller(s), {processCount} candidate process(es).";
    }

    /// <summary>
    /// Re-queries running processes just before the dropdown opens, not only on the explicit
    /// Refresh button — the previous selection can go stale between a refresh and clicking Start
    /// (e.g. a launcher process handing off to the real game process under a new PID), which
    /// surfaced as a confusing "No running process with id ..." error at Start time.
    /// </summary>
    private void CloakProcessCombo_DropDownOpened(object? sender, EventArgs e) => RefreshCloakProcessList();

    private int RefreshCloakProcessList()
    {
        var previouslySelected = (CloakProcessCombo.SelectedItem as CloakTargetProcess)?.Id;

        var selfId = Environment.ProcessId;
        var processes = Process.GetProcesses()
            .Where(p => p.Id != selfId && !string.IsNullOrWhiteSpace(SafeMainWindowTitle(p)))
            .Select(p => new CloakTargetProcess(p.Id, $"{SafeMainWindowTitle(p)} ({p.ProcessName}.exe, PID {p.Id})"))
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        CloakProcessCombo.ItemsSource = processes;

        var stillPresent = previouslySelected is { } id ? processes.FirstOrDefault(p => p.Id == id) : null;
        if (stillPresent is not null)
        {
            CloakProcessCombo.SelectedItem = stillPresent;
        }
        else if (processes.Count > 0)
        {
            CloakProcessCombo.SelectedIndex = 0;
        }

        return processes.Count;
    }

    private static string SafeMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void StartCloakButton_Click(object sender, RoutedEventArgs e)
    {
        if (CloakControllerCombo.SelectedItem is not ControllerDescriptor controller || controller.Backend != ControllerBackend.XInput)
        {
            MessageBox.Show("Select an XInput controller to hide. Only XInput controllers can be cloaked this way.", "No controller", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CloakProcessCombo.SelectedItem is not CloakTargetProcess target)
        {
            MessageBox.Show("Select a running process to hide the controller from.", "No process", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var userIndex = int.Parse(controller.Id.Split(':')[1], CultureInfo.InvariantCulture);

        try
        {
            _cloakService.Start(target.Id, userIndex);
            StartCloakButton.IsEnabled = false;
            StopCloakButton.IsEnabled = true;
            CloakStatusText.Text = $"Active — hiding {controller.DisplayName} from {target.DisplayName}";
            CloakStatusDot.Fill = Brushes.LimeGreen;
            StatusText.Text = "Cloak active.";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No running process", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                $"{target.DisplayName} isn't running anymore (its process id changed or it closed). " +
                "This happens if the list was refreshed before the target relaunched under a new PID — " +
                "click \"Refresh lists\" and pick it again.",
                "Process no longer running", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex.Message.Contains("last error 5", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                $"Windows denied access to {target.DisplayName} (error 5 = Access Denied). This almost always means " +
                "that process is running elevated (as Administrator — common for anti-cheat-protected games) while " +
                "236KO isn't. Close 236KO and relaunch it as Administrator (right-click the exe → Run as administrator), " +
                "then try again.",
                "Access denied — try running as Administrator", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start cloak: {ex.Message}", "Cloak failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopCloakButton_Click(object sender, RoutedEventArgs e)
    {
        _cloakService.Stop();
        StartCloakButton.IsEnabled = true;
        StopCloakButton.IsEnabled = false;
        CloakStatusText.Text = "Inactive";
        CloakStatusDot.Fill = Brushes.Gray;
        StatusText.Text = "Cloak stopped; controller visible to that process again.";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _captureCts?.Cancel();
        _cloakService.Dispose();
        StopEngine();
    }

    private sealed record CloakTargetProcess(int Id, string DisplayName);
}
