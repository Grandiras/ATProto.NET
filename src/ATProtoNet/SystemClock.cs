namespace ATProtoNet;

// The default clock. Layer 0 may not name TimeProvider.System, so this subclass stands in: a TimeProvider
// that overrides nothing reads the system clock.
internal sealed class SystemClock : TimeProvider
{
    public static readonly SystemClock Instance = new();

    private SystemClock() { }
}
