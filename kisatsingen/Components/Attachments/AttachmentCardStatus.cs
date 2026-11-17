namespace kisatsingen.Components.Attachments;

public abstract record AttachmentCardStatus
{
    private AttachmentCardStatus() { }

    public sealed record Waiting : AttachmentCardStatus;

    public sealed record Uploading(int Percent) : AttachmentCardStatus;

    public sealed record Processing : AttachmentCardStatus;

    public sealed record Failed(string Reason) : AttachmentCardStatus;
}
