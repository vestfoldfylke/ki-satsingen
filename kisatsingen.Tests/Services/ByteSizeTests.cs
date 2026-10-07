using System.Globalization;
using kisatsingen.Services;
using Xunit;

namespace kisatsingen.Tests.Services;

// Pinned to nb-NO, the culture the circuit runs these under.
public sealed class ByteSizeTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

    public ByteSizeTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO");

    public void Dispose() => CultureInfo.CurrentCulture = _originalCulture;

    // The bug this exists for: integer division showed a 1.5 MB limit as "1 MB".
    [Fact]
    public void A_size_that_is_not_a_whole_number_of_megabytes_keeps_its_decimal()
    {
        Assert.Equal("1,5 MB", ByteSize.Format(3 * ByteSize.Megabyte / 2));
    }

    [Fact]
    public void A_size_under_a_megabyte_is_shown_in_kilobytes_rather_than_as_zero()
    {
        Assert.Equal("500 kB", ByteSize.Format(500 * ByteSize.Kilobyte));
    }

    [Fact]
    public void A_size_under_a_kilobyte_is_shown_in_bytes()
    {
        Assert.Equal("512 B", ByteSize.Format(512));
    }
}
