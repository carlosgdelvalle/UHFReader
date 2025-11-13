using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Input;
using UhfPrime.TestBench.ViewModels;

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
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await _viewModel.DisposeAsync();
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
}
