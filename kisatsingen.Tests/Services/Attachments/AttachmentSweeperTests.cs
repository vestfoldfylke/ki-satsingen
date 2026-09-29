using kisatsingen.Services.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace kisatsingen.Tests.Services.Attachments;

public sealed class AttachmentSweeperTests : IDisposable
{
    private readonly ManualTime _time = new();
    private readonly AttachmentTestEnvironment _environment;
    private readonly AttachmentSweeper _sweeper;

    public AttachmentSweeperTests()
    {
        _environment = new AttachmentTestEnvironment(time: _time);
        _sweeper = new AttachmentSweeper(_environment.Registry, _environment.Store, _environment.Options, _time, NullLogger<AttachmentSweeper>.Instance);
    }

    public void Dispose()
    {
        _sweeper.Dispose();
        _environment.Dispose();
    }

    [Fact]
    public void An_attachment_never_sent_is_deleted_once_it_is_older_than_the_maximum_age()
    {
        var reservation = _environment.Registry.Reserve("owner", Guid.NewGuid(), "notat.md", 10)!;
        _environment.Registry.TryStartUpload(reservation.UploadId);
        var (file, stream) = _environment.Store.Create();
        stream.Dispose();
        _environment.Registry.Complete(reservation.UploadId, new UploadOutcome.Stored(file, 10, new string('a', 64)));
        _time.Advance(_environment.Options.TempFileMaxAge + TimeSpan.FromMinutes(1));

        _sweeper.Sweep();

        Assert.Empty(_environment.FilesOnDisk);
    }

    // A crash between creating a file and registering it leaves one nothing tracks.
    [Fact]
    public void A_temp_file_nothing_tracks_is_deleted_once_it_is_older_than_the_maximum_age()
    {
        var (_, stream) = _environment.Store.Create();
        stream.Dispose();
        _time.Advance(_environment.Options.TempFileMaxAge + TimeSpan.FromMinutes(1));

        _sweeper.Sweep();

        Assert.Empty(_environment.FilesOnDisk);
    }

    [Fact]
    public void A_recent_attachment_survives_the_sweep()
    {
        var scopeKey = Guid.NewGuid();
        _environment.Registry.Reserve("owner", scopeKey, "notat.md", 10);

        _sweeper.Sweep();

        Assert.Single(_environment.Registry.List("owner", scopeKey));
    }
}
