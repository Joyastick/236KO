namespace MotionInput.Core.Output;

public enum PrimitiveOutputKind
{
    ControllerButton,
    Key,

    /// <summary>
    /// A resolved direction token (bare digit, $controller_motion_final/start) that happened to be
    /// neutral (5) and so produced no ControllerButton entries of its own. Carries no button (Value
    /// is empty) but still marks "this combo declared a direction," so a combo can force the d-pad
    /// to neutral instead of it falling through to whatever the physical stick/d-pad is doing.
    /// </summary>
    DirectionOverride,
}

/// <summary>
/// A single resolved output action. For <see cref="PrimitiveOutputKind.ControllerButton"/>, Value is
/// one of: a,b,x,y,lb,rb,lt,rt,start,back,ls,rs,dpad_up,dpad_down,dpad_left,dpad_right.
/// For <see cref="PrimitiveOutputKind.Key"/>, Value is a keyboard key name (see <see cref="KeySender"/>).
/// For <see cref="PrimitiveOutputKind.DirectionOverride"/>, Value is always empty.
/// </summary>
public readonly record struct PrimitiveOutput(PrimitiveOutputKind Kind, string Value);
