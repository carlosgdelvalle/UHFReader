using SkiaSharp;
using Svg.Skia;
using System.Buffers.Binary;

static SKBitmap RenderSvg(string svgPath, int size)
{
    using var stream = File.OpenRead(svgPath);
    var svg = new SKSvg();
    svg.Load(stream);
    var pic = svg.Picture ?? throw new InvalidOperationException("SVG picture failed to load");
    var rect = pic.CullRect;
    var scale = Math.Min(size / rect.Width, size / rect.Height);
    var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
    var bmp = new SKBitmap(info);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);
    var matrix = SKMatrix.CreateScale(scale, scale);
    canvas.DrawPicture(pic, ref matrix);
    canvas.Flush();
    return bmp;
}

static void SavePng(SKBitmap bmp, string path)
{
    using var image = SKImage.FromBitmap(bmp);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var fs = File.Open(path, FileMode.Create, FileAccess.Write);
    data.SaveTo(fs);
}

// ICO writer that packs multiple PNG images (modern Windows supports PNG-in-ICO)
static void WriteIcoFromPngs(string outputPath, params string[] pngPaths)
{
    using var fs = File.Open(outputPath, FileMode.Create, FileAccess.Write);
    Span<byte> header = stackalloc byte[6];
    BinaryPrimitives.WriteUInt16LittleEndian(header[..2], 0); // reserved
    BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(2,2), 1); // type=icon
    BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4,2), (ushort)pngPaths.Length);
    fs.Write(header);

    var dirEntries = new List<(byte w, byte h, uint size, uint offset, byte[] data)>();
    foreach (var p in pngPaths)
    {
        var data = File.ReadAllBytes(p);
        using var png = SKBitmap.Decode(data);
        byte w = (byte)(png.Width == 256 ? 0 : png.Width);
        byte h = (byte)(png.Height == 256 ? 0 : png.Height);
        dirEntries.Add((w, h, (uint)data.Length, 0, data));
    }

    uint offset = (uint)(6 + 16 * dirEntries.Count);
    foreach (var e in dirEntries)
    {
        Span<byte> de = stackalloc byte[16];
        de[0] = e.w; // width (0 means 256)
        de[1] = e.h; // height
        de[2] = 0;   // color count
        de[3] = 0;   // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(de.Slice(4,2), 1); // planes
        BinaryPrimitives.WriteUInt16LittleEndian(de.Slice(6,2), 32); // bitcount
        BinaryPrimitives.WriteUInt32LittleEndian(de.Slice(8,4), e.size);
        BinaryPrimitives.WriteUInt32LittleEndian(de.Slice(12,4), offset);
        fs.Write(de);
        offset += e.size;
    }

    // write image data sequentially
    foreach (var e in dirEntries)
    {
        fs.Write(e.data, 0, e.data.Length);
    }
}

if (args.Length < 2)
{
    Console.WriteLine("Usage: IconGen <path-to-svg> <output-dir>");
    return;
}

var svgPath = Path.GetFullPath(args[0]);
var outDir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outDir);

// PNG sizes for .ico and diagnostics
int[] icoSizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var pngOutputs = new List<string>();
foreach (var s in icoSizes)
{
    using var bmp = RenderSvg(svgPath, s);
    var path = Path.Combine(outDir, $"icon_{s}.png");
    SavePng(bmp, path);
    pngOutputs.Add(path);
}

// ICO
var icoPath = Path.Combine(outDir, "app.ico");
WriteIcoFromPngs(icoPath, pngOutputs.ToArray());
Console.WriteLine($"ICO written: {icoPath}");

// macOS iconset for iconutil
var iconset = Path.Combine(outDir, "icon.iconset");
Directory.CreateDirectory(iconset);
var mapping = new (int Size, string Name)[]
{
    (16, "icon_16x16.png"),
    (32, "icon_16x16@2x.png"),
    (32, "icon_32x32.png"),
    (64, "icon_32x32@2x.png"),
    (128, "icon_128x128.png"),
    (256, "icon_128x128@2x.png"),
    (256, "icon_256x256.png"),
    (512, "icon_256x256@2x.png"),
    (512, "icon_512x512.png"),
    (1024, "icon_512x512@2x.png")
};

foreach (var (size, name) in mapping)
{
    using var bmp = RenderSvg(svgPath, size);
    SavePng(bmp, Path.Combine(iconset, name));
}
Console.WriteLine($"Iconset written: {iconset} (run: iconutil -c icns {iconset} -o {Path.Combine(outDir, "app.icns")})");

