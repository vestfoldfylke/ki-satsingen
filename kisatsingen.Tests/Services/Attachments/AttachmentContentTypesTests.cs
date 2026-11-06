using kisatsingen.Services.Attachments;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class AttachmentContentTypesTests
{
    [Fact]
    public void The_allowed_kinds_are_named_once_each_as_a_norwegian_list()
    {
        Assert.Equal("tekstfiler, bilder, PDF-filer og Word-dokumenter", AttachmentContentTypes.AllowedKindsText);
    }

    [Fact]
    public void The_allowed_kinds_with_extensions_list_each_kinds_extensions_after_its_name()
    {
        Assert.Equal(
            "tekstfiler (.txt, .md, .markdown), bilder (.png, .jpg, .jpeg), PDF-filer (.pdf) og Word-dokumenter (.docx)",
            AttachmentContentTypes.AllowedKindsWithExtensionsText);
    }
}
