using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using UhfPrime.Client;
using UhfPrime.Client.Protocol;
using UhfPrime.TestBench.Services;

namespace UhfPrime.TestBench.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly UhfPrimeReaderClient _client = new();
    private readonly Dictionary<string, TagViewModel> _tagsByEpc = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _tagLock = new();

    private string _host = "192.168.1.200";
    private int _port = 2022;
    private int _address = 0xFF;
    private double _minimumRssi = -70;
    private bool _isConnected;
    private bool _isInventoryRunning;
    private bool _isBeepEnabled = true;
    private string _trackedTag = string.Empty;
    private string _statusMessage = "Desconectado";
    private TagViewModel? _selectedTag;
    private int _powerDbm = 30;
    private UhfReaderParameters? _lastParameters;

    public MainWindowViewModel()
    {
        Console.WriteLine("[VM] MainWindowViewModel constructed");
        ConnectCommand = new AsyncCommand(ConnectAsync, () => !IsConnected);
        DisconnectCommand = new AsyncCommand(DisconnectAsync, () => IsConnected);
        StartInventoryCommand = new AsyncCommand(StartInventoryAsync, () => IsConnected && !IsInventoryRunning);
        StopInventoryCommand = new AsyncCommand(StopInventoryAsync, () => IsConnected && IsInventoryRunning);
        ClearTagsCommand = new RelayCommand(ClearTags);
        UseSelectedAsTrackedCommand = new RelayCommand(UseSelectedTag, () => SelectedTag is not null);
        ApplyPowerCommand = new AsyncCommand(ApplyPowerAsync, () => IsConnected);

        Tags = new ObservableCollection<TagViewModel>();
        FrameLog = new ObservableCollection<FrameLogEntry>();
        _client.TagReported += ClientOnTagReported;
        _client.FrameTraced += ClientOnFrameTraced;
    }

    public ObservableCollection<TagViewModel> Tags { get; }

    public ObservableCollection<FrameLogEntry> FrameLog { get; }

    public AsyncCommand ConnectCommand { get; }

    public AsyncCommand DisconnectCommand { get; }

    public AsyncCommand StartInventoryCommand { get; }

    public AsyncCommand StopInventoryCommand { get; }

    public RelayCommand ClearTagsCommand { get; }

    public RelayCommand UseSelectedAsTrackedCommand { get; }

    public AsyncCommand ApplyPowerCommand { get; }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public int Address
    {
        get => _address;
        set
        {
            var clamped = Math.Clamp(value, 0, 255);
            SetProperty(ref _address, clamped);
        }
    }

    public double MinimumRssi
    {
        get => _minimumRssi;
        set => SetProperty(ref _minimumRssi, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsInventoryRunning
    {
        get => _isInventoryRunning;
        private set
        {
            if (SetProperty(ref _isInventoryRunning, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsBeepEnabled
    {
        get => _isBeepEnabled;
        set => SetProperty(ref _isBeepEnabled, value);
    }

    public string TrackedTag
    {
        get => _trackedTag;
        set => SetProperty(ref _trackedTag, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int PowerDbm
    {
        get => _powerDbm;
        set
        {
            var clamped = Math.Clamp(value, 0, 33);
            SetProperty(ref _powerDbm, clamped);
        }
    }

    public TagViewModel? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (SetProperty(ref _selectedTag, value))
            {
                UseSelectedAsTrackedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client.TagReported -= ClientOnTagReported;
        _client.FrameTraced -= ClientOnFrameTraced;
        await StopInventorySafeAsync();
        await _client.DisposeAsync();
    }

    private async Task ConnectAsync()
    {
        try
        {
            await _client.ConnectAsync(Host, Port, (byte)Address);
            await RunOnUiThreadAsync(() =>
            {
                IsConnected = true;
                StatusMessage = $"Conectado a {Host}:{Port}. Solicitando parámetros...";
            });

            await FetchParametersAsync();
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = $"Connect failed: {ex.Message}";
            });
        }
    }

    private async Task FetchParametersAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var parameters = await _client.GetAllParametersAsync(cts.Token);
            await RunOnUiThreadAsync(() =>
            {
                _lastParameters = parameters;
                PowerDbm = parameters.RfidPower;
                StatusMessage = $"Conectado · Modo={parameters.WorkMode} · Q={parameters.QValue} · Potencia={PowerDbm} dBm";
            });
        }
        catch (OperationCanceledException)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = "Conectado, pero GET_ALL_PARAM agotó el tiempo.";
            });
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = $"Conectado, pero error al leer parámetros: {ex.Message}";
            });
        }
    }

    private async Task DisconnectAsync()
    {
        await StopInventorySafeAsync();
        await _client.DisconnectAsync();
        await RunOnUiThreadAsync(() =>
        {
            IsConnected = false;
            StatusMessage = "Desconectado";
        });
    }

    private async Task StartInventoryAsync()
    {
        await _client.StartInventoryAsync(InventoryRequest.Continuous);
        await RunOnUiThreadAsync(() =>
        {
            IsInventoryRunning = true;
            StatusMessage = "Inventario en curso";
        });
    }

    private async Task StopInventoryAsync()
    {
        await StopInventorySafeAsync();
    }

    private async Task StopInventorySafeAsync()
    {
        if (!IsInventoryRunning)
        {
            return;
        }

        try
        {
            await _client.StopInventoryAsync();
        }
        catch
        {
            // The reader silently stops inventory if the connection drops; ignore errors on shutdown.
        }

        await RunOnUiThreadAsync(() =>
        {
            IsInventoryRunning = false;
            StatusMessage = "Inventario detenido";
        });
    }

    private void ClearTags()
    {
        lock (_tagLock)
        {
            _tagsByEpc.Clear();
        }

        Tags.Clear();
    }

    private void UseSelectedTag()
    {
        if (SelectedTag is not null)
        {
            TrackedTag = SelectedTag.Epc;
        }
    }

    private void ClientOnTagReported(object? sender, TagReport report)
    {
        Console.WriteLine($"[VM] TagReported EPC={report.Epc} RSSI={report.Rssi:F1} Ant={report.Antenna} Ch={report.Channel}");
        if (report.Rssi < MinimumRssi)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(report.Epc))
        {
            return;
        }

        var normalized = report.Epc.ToUpperInvariant();
        var isNew = false;
        TagViewModel tagVm;
        lock (_tagLock)
        {
            if (!_tagsByEpc.TryGetValue(normalized, out tagVm!))
            {
                tagVm = new TagViewModel(report.Epc, report.Uid);
                _tagsByEpc[normalized] = tagVm;
                isNew = true;
            }
        }

        if (isNew)
        {
            var localVm = tagVm;
            Dispatcher.UIThread.Post(() => Tags.Add(localVm));
        }

        var targetVm = tagVm;
        Dispatcher.UIThread.Post(() => targetVm.Update(report));

        var tracked = TrackedTag?.Trim();
        if (IsBeepEnabled && !string.IsNullOrWhiteSpace(tracked))
        {
            var trackedNormalized = tracked.ToUpperInvariant();
            if (normalized.Equals(trackedNormalized, StringComparison.Ordinal))
            {
                BeepService.TryBeep();
            }
        }
    }

    private void ClientOnFrameTraced(object? sender, FrameTraceEventArgs e)
    {
        var summary = $"CMD=0x{e.Command:X4} STATUS=0x{e.Status:X2} LEN={e.PayloadLength} Valid={e.IsValid}";
        var hex = BitConverter.ToString(e.Raw).Replace('-', ' ');
        var entry = new FrameLogEntry(DateTimeOffset.Now, e.Note, summary, hex);
        Console.WriteLine($"[VM] FrameTraced {e.Note} {summary}");
        Dispatcher.UIThread.Post(() =>
        {
            FrameLog.Insert(0, entry);
            while (FrameLog.Count > 200)
            {
                FrameLog.RemoveAt(FrameLog.Count - 1);
            }
        });
    }

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        StartInventoryCommand.RaiseCanExecuteChanged();
        StopInventoryCommand.RaiseCanExecuteChanged();
        ApplyPowerCommand.RaiseCanExecuteChanged();
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private async Task ApplyPowerAsync()
    {
        try
        {
            var parameters = _lastParameters;
            if (parameters is null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                parameters = await _client.GetAllParametersAsync(cts.Token);
            }

            var payload = (parameters ?? new UhfReaderParameters()).ToPayload();
            // Byte 15 per manual = RF power dBm
            payload[15] = (byte)PowerDbm;
            var updated = UhfReaderParameters.FromPayload(payload);

            using var setCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _client.SetAllParametersAsync(updated, setCts.Token);
            _lastParameters = updated;

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = $"Potencia actualizada a {PowerDbm} dBm";
            });
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = $"Error al aplicar potencia: {ex.Message}";
            });
        }
    }
}
