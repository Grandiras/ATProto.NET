using System.Numerics;

namespace ATProtoNet.Data;

// The check every reader limit option shares.
internal static class OptionLimits
{
    public static T Positive<T>(T value) where T : INumber<T> =>
        value > T.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A reader limit must be positive.");
}
