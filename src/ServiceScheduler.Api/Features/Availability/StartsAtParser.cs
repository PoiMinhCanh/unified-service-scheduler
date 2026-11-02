using System.Globalization;

namespace ServiceScheduler.Api.Features.Availability;

/// <summary>
/// Parses a raw <c>startsAt</c> value only when it carries a trailing <c>Z</c> or an explicit numeric
/// offset. Offset-free and date-only values are rejected before parsing so model binding can never
/// silently interpret them as host-local time.
/// </summary>
public static class StartsAtParser
{
    public static bool TryParse(string? rawValue, out DateTimeOffset value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (!HasExplicitOffset(trimmed))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    private static bool HasExplicitOffset(string value)
    {
        if (value.EndsWith('Z') || value.EndsWith('z'))
        {
            return true;
        }

        if (value.Length >= 6)
        {
            var colonOffset = value[^6..];
            if (colonOffset[0] is '+' or '-'
                && colonOffset[1..3].All(char.IsAsciiDigit)
                && colonOffset[3] == ':'
                && colonOffset[4..6].All(char.IsAsciiDigit))
            {
                return true;
            }
        }

        if (value.Length >= 5)
        {
            var compactOffset = value[^5..];
            if (compactOffset[0] is '+' or '-'
                && compactOffset[1..].All(char.IsAsciiDigit))
            {
                return true;
            }
        }

        return false;
    }
}
