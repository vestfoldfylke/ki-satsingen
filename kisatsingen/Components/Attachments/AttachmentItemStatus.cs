namespace kisatsingen.Components.Attachments;

public abstract record AttachmentItemStatus
{
    private AttachmentItemStatus() { }

    public sealed record Waiting : AttachmentItemStatus;

    public sealed record Uploading(int Percent) : AttachmentItemStatus;

    public sealed record Processing : AttachmentItemStatus;

    public sealed record Failed(string Reason) : AttachmentItemStatus;
}
