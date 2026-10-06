namespace kisatsingen.Services.Attachments;

// A place counted against the owner's limits from the moment it is granted,
// held until the upload completes, is rejected or is removed. Cancellation
// fires on removal, so an upload in flight stops with it.
public sealed record UploadReservation(Guid UploadId, AttachmentType Type, CancellationToken Cancellation);
