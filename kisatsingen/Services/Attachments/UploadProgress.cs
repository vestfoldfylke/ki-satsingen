namespace kisatsingen.Services.Attachments;

// Reports an upload's received bytes on the uploading thread, in order.
// Progress<T> posts each report to the thread pool, so they could arrive out of order.
internal sealed class UploadProgress(Action<long> handler) : IProgress<long>
{
    public void Report(long value) => handler(value);
}
