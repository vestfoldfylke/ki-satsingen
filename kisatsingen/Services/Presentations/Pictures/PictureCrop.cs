namespace kisatsingen.Services.Presentations.Pictures;

// How much to cut from each edge, in DrawingML's thousandths of a percent
// (100 000 = the whole side), so a picture fills its frame without being
// stretched: the overflowing dimension is trimmed equally from both sides.
public readonly record struct PictureCrop(int Left, int Top, int Right, int Bottom)
{
    private const double WholeSide = 100_000;

    public static readonly PictureCrop None = new(0, 0, 0, 0);

    public static PictureCrop ForCover(long imageWidth, long imageHeight, long frameWidth, long frameHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || frameWidth <= 0 || frameHeight <= 0)
        {
            return None;
        }

        var imageAspect = (double)imageWidth / imageHeight;
        var frameAspect = (double)frameWidth / frameHeight;

        if (imageAspect > frameAspect)
        {
            var side = EdgeCrop(frameAspect / imageAspect);
            return new PictureCrop(side, 0, side, 0);
        }

        var edge = EdgeCrop(imageAspect / frameAspect);
        return new PictureCrop(0, edge, 0, edge);
    }

    private static int EdgeCrop(double visibleFraction) =>
        (int)Math.Round((1 - visibleFraction) / 2 * WholeSide);
}
