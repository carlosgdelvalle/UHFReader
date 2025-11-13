using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Input;
using UhfPrime.TestBench.ViewModels;
using SkiaSharp;
using Svg.Skia;
using System.IO;

namespace UhfPrime.TestBench;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private Image? _logoImage;
    private bool _useAltLogo;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Console.WriteLine($"[Window] DataContext set: {_viewModel?.GetType().Name}");
        _logoImage = this.FindControl<Image>("LogoImage");
        TrySetWindowIconFromSvg();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await _viewModel.DisposeAsync();
    }

    private void TrySetWindowIconFromSvg()
    {
        try
        {
            var uri = new Uri("avares://UhfPrime.TestBench/Assets/rfidIcon.svg");
            using var stream = AssetLoader.Open(uri);
            // Load SVG and render to a 256x256 bitmap
            var svg = new SKSvg();
            svg.Load(stream);
            var picture = svg.Picture;
            if (picture is null)
                return;

            const int size = 256;
            var rect = picture.CullRect;
            var scale = Math.Min(size / rect.Width, size / rect.Height);
            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            var matrix = SKMatrix.CreateScale(scale, scale);
            canvas.DrawPicture(picture, ref matrix);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            var bmp = new Bitmap(ms);
            this.Icon = new WindowIcon(bmp);
        }
        catch
        {
            // Ignore failures (e.g., package not present); window will keep default icon
        }
    }

    private void LogoImage_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_logoImage is null)
        {
            return;
        }

        _useAltLogo = !_useAltLogo;
        var uri = new Uri(_useAltLogo
            ? "avares://UhfPrime.TestBench/Assets/logoCubo.png"
            : "avares://UhfPrime.TestBench/Assets/logoldm.jpeg");
        try
        {
            using var stream = AssetLoader.Open(uri);
            _logoImage.Source = new Bitmap(stream);
        }
        catch
        {
            // ignore asset loading failures
        }
    }

    private void TagsGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            // Prefer the DataContext of the clicked element
            if (e.Source is Control c && c.DataContext is ViewModels.TagViewModel rowVm)
            {
                _viewModel.TrackedTag = rowVm.Epc;
                return;
            }

            // Fallback: use current selection
            if (sender is DataGrid grid && grid.SelectedItem is ViewModels.TagViewModel selected)
            {
                _viewModel.TrackedTag = selected.Epc;
            }
        }
        catch
        {
            // ignore
        }
    }
}
