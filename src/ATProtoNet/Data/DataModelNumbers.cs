using System.Globalization;

namespace ATProtoNet.Data;

// The one place the data model decides what a number is (D-26): a signed 64-bit integer, nothing else.
// A native float kind, once upstream defines one, is added here and in the kinds that carry it.
internal static class DataModelNumbers
{
    // A JSON number as an integer. 123.0 is the integer 123 (atproto-interop-tests data-model-valid);
    // a non-zero fraction or any exponent is a float, and the data model has none.
    public static long FromJson(ReadOnlySpan<byte> number)
    {
        if (number.IndexOfAny("eE"u8) >= 0) throw NotAnInteger();
        var point = number.IndexOf((byte)'.');
        if (point >= 0)
        {
            if (number[(point + 1)..].IndexOfAnyExcept((byte)'0') >= 0) throw NotAnInteger();
            number = number[..point];
        }
        return long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw OutOfRange();
    }

    public static FormatException NotAnInteger() => new("Floating point numbers are not part of the atproto data model; numbers are signed 64-bit integers.");

    public static FormatException OutOfRange() => new("The integer is outside the signed 64-bit range of the atproto data model.");
}
