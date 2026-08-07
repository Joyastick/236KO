using System.Text.Json;
using System.Text.Json.Serialization;
using MotionInput.Core.Models;
using MotionInput.Core.Motion;

namespace MotionInput.Core.Profiles;

/// <summary>Loads/saves profiles as JSON files in a "Profiles" folder next to the app, and knows how to build the built-in default.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _directory;

    public ProfileStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "Profiles");
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public IReadOnlyList<string> ListProfileNames() =>
        Directory.EnumerateFiles(_directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public Profile Load(string name)
    {
        var path = PathFor(name);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Profile>(json, JsonOptions) ?? new Profile { Name = name };
    }

    public void Save(Profile profile)
    {
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(PathFor(profile.Name), json);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public bool Exists(string name) => File.Exists(PathFor(name));

    private string PathFor(string name) => Path.Combine(_directory, $"{SanitizeFileName(name)}.json");

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    /// <summary>Ships a fighting-game-standard starter profile: qcf/qcb/dp/rdp/half-circles, mapped to A/B on the virtual pad.</summary>
    public static Profile CreateDefault(string name = "Default")
    {
        var profile = new Profile
        {
            Name = name,
            Motions = new List<MotionDefinition>
            {
                new() { Name = "dp", Sequence = new() { 6, 2, 3 } },
                new() { Name = "rdp", Sequence = new() { 4, 2, 1 } },
                new() { Name = "half_circle_forward", Sequence = new() { 4, 1, 2, 3, 6 } },
                new() { Name = "half_circle_back", Sequence = new() { 6, 3, 2, 1, 4 } },
                new() { Name = "qcf", Sequence = new() { 2, 3, 6 } },
                new() { Name = "qcb", Sequence = new() { 2, 1, 4 } },
            },
            AttackBindings = new Dictionary<string, List<string>>
            {
                ["light"] = new() { "x" },
                ["medium"] = new() { "y" },
                ["heavy"] = new() { "rb" },
            },
            MotionAttackOutputs = new Dictionary<string, Dictionary<string, List<string>>>
            {
                ["qcf"] = new()
                {
                    ["light"] = new() { "controller_direction:6", "controller:a" },
                    ["medium"] = new() { "$controller_motion_final", "controller:b" },
                    ["heavy"] = new() { "controller:a", "controller:b" },
                },
                ["qcb"] = new()
                {
                    ["light"] = new() { "controller_direction:4", "controller:a" },
                    ["medium"] = new() { "$controller_motion_final", "controller:b" },
                    ["heavy"] = new() { "controller:a", "controller:b" },
                },
                ["dp"] = new()
                {
                    ["light"] = new() { "controller_direction:6", "controller:a" },
                    ["medium"] = new() { "controller_direction:2", "controller:b" },
                    ["heavy"] = new() { "controller_direction:2", "controller:a" },
                },
                ["rdp"] = new()
                {
                    ["light"] = new() { "controller_direction:4", "controller:a" },
                    ["medium"] = new() { "controller_direction:2", "controller:b" },
                    ["heavy"] = new() { "controller_direction:2", "controller:a" },
                },
                ["half_circle_forward"] = new()
                {
                    ["light"] = new() { "controller:x", "controller:a" },
                    ["medium"] = new() { "controller:x", "controller:b" },
                    ["heavy"] = new() { "controller:a", "controller:b" },
                },
                ["half_circle_back"] = new()
                {
                    ["light"] = new() { "controller:x", "controller:a" },
                    ["medium"] = new() { "controller:x", "controller:b" },
                    ["heavy"] = new() { "controller:a", "controller:b" },
                },
            },
            AttackOutputs = new Dictionary<string, List<string>>
            {
                ["light"] = new() { "controller:x" },
                ["medium"] = new() { "controller:y" },
                ["heavy"] = new() { "controller:rb" },
            },
            KeyOutputs = new Dictionary<string, List<string>>
            {
                ["a"] = new() { "controller:a" },
                ["b"] = new() { "controller:b" },
                ["lb"] = new() { "controller:lb" },
                ["ls"] = new() { "controller:ls" },
                ["rs"] = new() { "controller:rs" },
                ["start"] = new() { "controller:start" },
                ["back"] = new() { "controller:back" },
            },
        };

        return profile;
    }
}
