namespace kisatsingen.Services.Presentations.Pictures;

// Where a picture goes when it is shown whole inside its frame: as large as
// fits, centred, keeping its proportions. In EMUs, as the frame is given.
public readonly record struct PicturePlacement(long X, long Y, long Width, long Height)
{
    public static PicturePlacement Contain(long imageWidth, long imageHeight, long frameX, long frameY, long frameWidth, long frameHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || frameWidth <= 0 || frameHeight <= 0)
        {
            return new PicturePlacement(frameX, frameY, frameWidth, frameHeight);
        }

        var scale = Math.Min((double)frameWidth / imageWidth, (double)frameHeight / imageHeight);
        var width = (long)Math.Round(imageWidth * scale);
        var height = (long)Math.Round(imageHeight * scale);

        return new PicturePlacement(frameX + (frameWidth - width) / 2, frameY + (frameHeight - height) / 2, width, height);
    }
}
