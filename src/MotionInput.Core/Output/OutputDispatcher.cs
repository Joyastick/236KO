namespace MotionInput.Core.Output;

/// <summary>Presses a resolved set of outputs, holds them briefly, then releases — enough for the target game to register a frame of input.</summary>
public sealed class OutputDispatcher
{
    private readonly IVirtualGamepad _gamepad;

    public OutputDispatcher(IVirtualGamepad gamepad)
    {
        _gamepad = gamepad;
    }

    public async Task FireAsync(IReadOnlyList<PrimitiveOutput> outputs, int holdMs, CancellationToken cancellationToken = default)
    {
        if (outputs.Count == 0) return;

        foreach (var output in outputs)
        {
            Apply(output, true);
        }

        try
        {
            await Task.Delay(holdMs, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // still release below even if cancelled mid-hold
        }

        foreach (var output in outputs)
        {
            Apply(output, false);
        }
    }

    private void Apply(PrimitiveOutput output, bool pressed)
    {
        if (output.Kind == PrimitiveOutputKind.ControllerButton)
        {
            _gamepad.SetButton(output.Value, pressed);
        }
        else
        {
            KeySender.SetKey(output.Value, pressed);
        }
    }
}
