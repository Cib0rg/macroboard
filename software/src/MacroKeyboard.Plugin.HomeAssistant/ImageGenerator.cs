using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MacroKeyboard.Plugin.HomeAssistant;

/// <summary>
/// Generates 160×160 PNG button images for HA entity states.
/// The backend will add the purple plugin ring overlay on top of this image.
/// All content is therefore kept in the inner ~130px area to avoid the ring.
/// </summary>
public static class ImageGenerator
{
    private const int Size = 160;

    // State colours
    private static readonly Color ColorOn  = Color.FromRgb(0x2e, 0xcc, 0x71); // green
    private static readonly Color ColorOff = Color.FromRgb(0x55, 0x55, 0x55); // dark gray
    private static readonly Color ColorBg  = Color.FromRgb(0x12, 0x12, 0x12); // near-black

    /// <summary>
    /// Generates a button image showing the entity label and on/off state.
    /// Returns raw PNG bytes (no data-URL prefix).
    /// </summary>
    public static byte[] Generate(string label, string state, bool isOn)
    {
        using var image = new Image<Rgba32>(Size, Size);
        image.Mutate(ctx =>
        {
            // Background
            ctx.Fill(ColorBg);

            // Indicator circle (upper half, radius 28)
            var indicatorColor = isOn ? ColorOn : ColorOff;
            ctx.Fill(indicatorColor, new SixLabors.ImageSharp.Drawing.EllipsePolygon(80f, 58f, 28f, 28f));

            // Label text (centre of image)
            TryDrawText(ctx, label, 22, new PointF(80, 100), Color.White);

            // State badge ("ON" / "OFF") — smaller, below label
            var stateBadge = state.Length > 8 ? state[..8] : state.ToUpperInvariant();
            TryDrawText(ctx, stateBadge, 16, new PointF(80, 128), isOn ? ColorOn : ColorOff);
        });

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>Generates a placeholder image for an unconfigured button.</summary>
    public static byte[] GeneratePlaceholder()
    {
        using var image = new Image<Rgba32>(Size, Size);
        image.Mutate(ctx =>
        {
            ctx.Fill(ColorBg);
            TryDrawText(ctx, "HA", 36, new PointF(80, 72), Color.FromRgb(0x66, 0x66, 0x66));
            TryDrawText(ctx, "No entity", 16, new PointF(80, 110), Color.FromRgb(0x44, 0x44, 0x44));
        });
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static void TryDrawText(IImageProcessingContext ctx, string text, float size, PointF origin, Color color)
    {
        try
        {
            FontFamily family;
            try        { family = SystemFonts.Get("Arial"); }
            catch      { try { family = SystemFonts.Get("DejaVu Sans"); }
                         catch { family = SystemFonts.Families.First(); } }

            var font = family.CreateFont(size, FontStyle.Bold);
            var opts = new RichTextOptions(font)
            {
                Origin              = origin,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                WrappingLength      = Size - 20,
                TextAlignment       = TextAlignment.Center
            };
            ctx.DrawText(opts, text, color);
        }
        catch
        {
            // Font rendering failure is non-fatal — button will just show the indicator circle
        }
    }
}
