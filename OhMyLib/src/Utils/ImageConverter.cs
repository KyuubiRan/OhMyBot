using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace OhMyLib.Utils;

public static class ImageConverter
{
    public static void ToWebp(
        Stream input,
        Stream output,
        ResizeOptions? resizeOptions = null,
        int quality = 100
    )
    {
        using var image = Image.Load(input);

        image.Mutate(ctx =>
        {
            if (resizeOptions != null)
                ctx.Resize(resizeOptions);
        });

        var encoder = new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = quality,
        };

        image.Save(output, encoder);
    }

    public static void ToPng(
        Stream input,
        Stream output,
        ResizeOptions? resizeOptions = null
    )
    {
        using var image = Image.Load(input);

        image.Mutate(ctx =>
        {
            if (resizeOptions != null)
                ctx.Resize(resizeOptions);
        });

        image.SaveAsPng(output);
    }

    public static void ToJpeg(
        Stream input,
        Stream output,
        ResizeOptions? resizeOptions = null,
        int quality = 100
    )
    {
        using var image = Image.Load(input);

        image.Mutate(ctx =>
        {
            if (resizeOptions != null)
                ctx.Resize(resizeOptions);
        });

        var encoder = new JpegEncoder
        {
            Quality = quality,
        };

        image.Save(output, encoder);
    }
}