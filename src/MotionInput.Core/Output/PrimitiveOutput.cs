namespace MotionInput.Core.Output;

public enum PrimitiveOutputKind
{
    ControllerButton,
    Key,
}

/// <summary>
/// A single resolved output action. For <see cref="PrimitiveOutputKind.ControllerButton"/>, Value is
/// one of: a,b,x,y,lb,rb,lt,rt,start,back,ls,rs,dpad_up,dpad_down,dpad_left,dpad_right.
/// For <see cref="PrimitiveOutputKind.Key"/>, Value is a keyboard key name (see <see cref="KeySender"/>).
/// </summary>
public readonly record struct PrimitiveOutput(PrimitiveOutputKind Kind, string Value);
