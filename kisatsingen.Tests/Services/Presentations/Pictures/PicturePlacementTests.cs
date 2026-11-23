using kisatsingen.Services.Presentations.Pictures;
using Xunit;

namespace kisatsingen.Tests.Services.Presentations.Pictures;

public sealed class PicturePlacementTests
{
    [Fact]
    public void A_tall_picture_in_a_wide_frame_takes_the_frames_height_and_is_centred_across_it()
    {
        var placement = PicturePlacement.Contain(imageWidth: 100, imageHeight: 200, frameX: 1_000, frameY: 500, frameWidth: 800, frameHeight: 400);

        Assert.Equal(new PicturePlacement(X: 1_300, Y: 500, Width: 200, Height: 400), placement);
    }

    [Fact]
    public void A_wide_picture_in_a_tall_frame_takes_the_frames_width_and_is_centred_down_it()
    {
        var placement = PicturePlacement.Contain(imageWidth: 400, imageHeight: 100, frameX: 0, frameY: 0, frameWidth: 400, frameHeight: 400);

        Assert.Equal(new PicturePlacement(X: 0, Y: 150, Width: 400, Height: 100), placement);
    }

    [Fact]
    public void A_picture_with_the_frames_shape_fills_the_frame_exactly()
    {
        var placement = PicturePlacement.Contain(imageWidth: 1920, imageHeight: 1080, frameX: 10, frameY: 20, frameWidth: 12_192_000, frameHeight: 6_858_000);

        Assert.Equal(new PicturePlacement(X: 10, Y: 20, Width: 12_192_000, Height: 6_858_000), placement);
    }

    [Fact]
    public void A_picture_without_a_size_takes_the_whole_frame()
    {
        var placement = PicturePlacement.Contain(imageWidth: 0, imageHeight: 0, frameX: 10, frameY: 20, frameWidth: 300, frameHeight: 200);

        Assert.Equal(new PicturePlacement(X: 10, Y: 20, Width: 300, Height: 200), placement);
    }
}
