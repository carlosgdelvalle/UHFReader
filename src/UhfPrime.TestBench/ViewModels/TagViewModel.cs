using System;

namespace UhfPrime.TestBench.ViewModels;

public sealed class TagViewModel : ObservableObject
{
    private double _rssi;
    private DateTimeOffset _lastSeen;
    private int _readCount;
    private byte _antenna;
    private byte _channel;

    public TagViewModel(string epc, string uid)
    {
        Epc = epc;
        Uid = uid;
        _lastSeen = DateTimeOffset.UtcNow;
    }

    public string Epc { get; }

    public string Uid { get; }

    public double Rssi
    {
        get => _rssi;
        private set
        {
            if (SetProperty(ref _rssi, value))
            {
                RaisePropertyChanged(nameof(RssiDisplay));
            }
        }
    }

    public string RssiDisplay => $"{Rssi:F1} dBm";

    public DateTimeOffset LastSeen
    {
        get => _lastSeen;
        private set => SetProperty(ref _lastSeen, value);
    }

    public int ReadCount
    {
        get => _readCount;
        private set => SetProperty(ref _readCount, value);
    }

    public byte Antenna
    {
        get => _antenna;
        private set => SetProperty(ref _antenna, value);
    }

    public byte Channel
    {
        get => _channel;
        private set => SetProperty(ref _channel, value);
    }

    public void Update(UhfPrime.Client.TagReport report)
    {
        Rssi = report.Rssi;
        LastSeen = report.Timestamp;
        ReadCount += 1;
        Antenna = report.Antenna;
        Channel = report.Channel;
    }
}
