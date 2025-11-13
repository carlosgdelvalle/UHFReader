using System;
using Avalonia.Controls;
using UhfPrime.TestBench.ViewModels;

namespace UhfPrime.TestBench;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Console.WriteLine($"[Window] DataContext set: {_viewModel?.GetType().Name}");
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await _viewModel.DisposeAsync();
    }
}
