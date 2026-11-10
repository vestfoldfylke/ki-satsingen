using System.Globalization;

namespace kisatsingen.Services;

public static class DateTimeService
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    public static string GetTimeZonedDateTimeOffsetString(DateTimeOffset dateTimeOffset, string dateTimeFormat = "dd.MM.yyyy HH:mm:ss") =>
        TimeZoneInfo.ConvertTime(dateTimeOffset, Oslo).ToString(dateTimeFormat, CultureInfo.CurrentCulture);

    public static DateTimeOffset GetTimeZonedDateTimeOffset(DateTimeOffset dateTimeOffset) =>
        TimeZoneInfo.ConvertTime(dateTimeOffset, Oslo);
}
