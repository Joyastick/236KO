namespace MotionInput.Core.Input;

/// <summary>Identifies a controller the user can pick in the UI. <see cref="Id"/> is what gets persisted in a profile.</summary>
public sealed record ControllerDescriptor(string Id, string DisplayName, ControllerBackend Backend);
