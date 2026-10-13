using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

// For tests of Unix file modes and links: Windows has no modes, and creating a
// link there needs Developer Mode or an administrator.
internal sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Needs Unix file modes and links.";
        }
    }
}
