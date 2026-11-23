using kisatsingen.Services.Presentations.Pictures;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations.Pictures;

public sealed class PictureCropTests
{
    [Fact]
    public void A_wide_picture_in_a_square_frame_loses_a_quarter_of_its_width_on_each_side()
    {
        var crop = PictureCrop.ForCover(imageWidth: 2000, imageHeight: 1000, frameWidth: 500, frameHeight: 500);

        Assert.Equal(new PictureCrop(25_000, 0, 25_000, 0), crop);
    }

    [Fact]
    public void A_tall_picture_in_a_square_frame_loses_a_quarter_of_its_height_at_top_and_bottom()
    {
        var crop = PictureCrop.ForCover(imageWidth: 1000, imageHeight: 2000, frameWidth: 500, frameHeight: 500);

        Assert.Equal(new PictureCrop(0, 25_000, 0, 25_000), crop);
    }

    [Fact]
    public void A_picture_with_the_frames_shape_is_not_cropped()
    {
        var crop = PictureCrop.ForCover(imageWidth: 1920, imageHeight: 1080, frameWidth: 12_192_000, frameHeight: 6_858_000);

        Assert.Equal(PictureCrop.None, crop);
    }

    [Fact]
    public void A_picture_or_frame_without_a_size_is_not_cropped()
    {
        var crop = PictureCrop.ForCover(imageWidth: 1920, imageHeight: 1080, frameWidth: 0, frameHeight: 6_858_000);

        Assert.Equal(PictureCrop.None, crop);
    }
}
