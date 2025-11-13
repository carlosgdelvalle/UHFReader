using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UhfPrime.Client.Protocol;

namespace UhfPrime.Client;

/// <summary>
/// Thin protocol client for the UHF Prime reader frames documented in AGENTS.md.
/// </summary>
public sealed class UhfPrimeReaderClient : IAsyncDisposable
{
    private const byte FrameHead = 0xCF;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<ushort, Queue<TaskCompletionSource<UhfPrimeFrame>>> _waiters = new();
    private readonly object _waiterLock = new();

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;

    public event EventHandler<TagReport>? TagReported;
    public event EventHandler<UhfPrimeFrame>? FrameReceived;
    public event EventHandler<FrameTraceEventArgs>? FrameTraced;

    public bool IsConnected => _tcpClient?.Connected == true;

    public string? Host { get; private set; }

    public int Port { get; private set; }

    public byte Address { get; private set; } = 0xFF;

    public async Task ConnectAsync(string host, int port, byte address = 0xFF, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);

        Host = host;
        Port = port;
        Address = address;

        _tcpClient = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await _tcpClient.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
        _stream = _tcpClient.GetStream();

        _readerCts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReaderLoopAsync(_readerCts.Token));
    }

    public async Task DisconnectAsync()
    {
        _readerCts?.Cancel();
        if (_readerTask is not null)
        {
            await _readerTask.ConfigureAwait(false);
        }

        _readerCts?.Dispose();
        _readerCts = null;
        _readerTask = null;

        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _tcpClient?.Close();
        _tcpClient?.Dispose();
        _tcpClient = null;

        lock (_waiterLock)
        {
            foreach (var queue in _waiters.Values)
            {
                while (queue.Count > 0)
                {
                    queue.Dequeue().TrySetCanceled();
                }
            }
            _waiters.Clear();
        }
    }

    public async Task<UhfReaderParameters> GetAllParametersAsync(CancellationToken cancellationToken = default)
    {
        var frame = await SendAndWaitAsync(UhfPrimeCommand.GetAllParameters, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        return UhfReaderParameters.FromPayload(frame.Payload);
    }

    public async Task SetAllParametersAsync(UhfReaderParameters parameters, CancellationToken cancellationToken = default)
    {
        var frame = await SendAndWaitAsync(UhfPrimeCommand.SetAllParameters, parameters.ToPayload(), cancellationToken).ConfigureAwait(false);
        if (!frame.IsSuccess)
        {
            throw new InvalidOperationException($"Reader returned error status 0x{frame.Status:X2} to SET_ALL_PARAM.");
        }
    }

    public async Task StartInventoryAsync(InventoryRequest request, CancellationToken cancellationToken = default)
    {
        await SendAsync(UhfPrimeCommand.InventoryIsoContinue, request.ToPayload(), cancellationToken).ConfigureAwait(false);
    }

    public async Task StopInventoryAsync(CancellationToken cancellationToken = default)
    {
        var frame = await SendAndWaitAsync(UhfPrimeCommand.InventoryStop, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        if (!frame.IsSuccess)
        {
            throw new InvalidOperationException($"Reader returned error status 0x{frame.Status:X2} to INVENTORY_STOP.");
        }
    }

    public async Task PulseRelayAsync(bool closeRelay, CancellationToken cancellationToken = default)
    {
        var payload = new byte[]
        {
            0x02,
            closeRelay ? (byte)0x00 : (byte)0x01
        };
        var frame = await SendAndWaitAsync(UhfPrimeCommand.RelayControl, payload, cancellationToken).ConfigureAwait(false);
        if (!frame.IsSuccess)
        {
            throw new InvalidOperationException($"Reader returned error status 0x{frame.Status:X2} to RELAY control.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    private async Task ReaderLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    continue;
                }

                FrameReceived?.Invoke(this, frame);
                if (!TryResumeWaiter(frame))
                {
                    DispatchUnsolicitedFrame(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool TryResumeWaiter(UhfPrimeFrame frame)
    {
        TaskCompletionSource<UhfPrimeFrame>? waiter = null;
        lock (_waiterLock)
        {
            if (_waiters.TryGetValue(frame.Command, out var queue) && queue.Count > 0)
            {
                waiter = queue.Dequeue();
                if (queue.Count == 0)
                {
                    _waiters.Remove(frame.Command);
                }
            }
        }

        if (waiter is null)
        {
            return false;
        }

        if (!waiter.TrySetResult(frame))
        {
            return TryResumeWaiter(frame);
        }

        return true;
    }

    private void DispatchUnsolicitedFrame(UhfPrimeFrame frame)
    {
        if (frame.Command == (ushort)UhfPrimeCommand.InventoryIsoContinue)
        {
            if (frame.Status == 0x00 && TagReport.TryParseInventoryPayload(frame.Payload, out var report) && report is not null)
            {
                TagReported?.Invoke(this, report);
            }
            else if (frame.Status == 0x00)
            {
                var payloadHex = BitConverter.ToString(frame.Payload).Replace("-", " ");
                Console.WriteLine($"[WARN] Failed to parse INVENTORY payload LEN={frame.Payload.Length} HEX={payloadHex}");
            }

            return;
        }
    }

    private async Task<UhfPrimeFrame?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return null;
        }

        var header = new byte[5];
        var head = new byte[1];

        do
        {
            await _stream.ReadExactlyAsync(head, cancellationToken).ConfigureAwait(false);
        } while (head[0] != FrameHead);

        header[0] = FrameHead;
        await _stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken).ConfigureAwait(false);

        var length = header[4];
        var remainder = new byte[length + 2];
        await _stream.ReadExactlyAsync(remainder, cancellationToken).ConfigureAwait(false);

        var raw = new byte[header.Length + remainder.Length];
        Array.Copy(header, 0, raw, 0, header.Length);
        Array.Copy(remainder, 0, raw, header.Length, remainder.Length);

        var crcCalculated = Crc16.Compute(raw.AsSpan(0, raw.Length - 2));
        var crcReceived = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(raw.Length - 2));

        var address = raw[1];
        var command = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(2));
        byte status = length > 0 ? raw[5] : (byte)0x00;
        var payloadLength = Math.Max(0, length - 1);
        var isValid = crcCalculated == crcReceived;
        TraceFrame(raw, isValid, address, command, status, payloadLength, isValid ? "RX" : "RX CRC mismatch");
        if (!isValid)
        {
            return null;
        }

        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            Array.Copy(raw, 6, payload, 0, payloadLength);
        }

        return new UhfPrimeFrame(address, command, status, payload, raw);
    }

    private async Task<UhfPrimeFrame> SendAndWaitAsync(UhfPrimeCommand command, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var waiter = RegisterWaiter((ushort)command, cancellationToken);
        try
        {
            await SendAsync(command, payload, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RemoveWaiter((ushort)command, waiter);
            throw;
        }

        return await waiter.Task.ConfigureAwait(false);
    }

    private TaskCompletionSource<UhfPrimeFrame> RegisterWaiter(ushort command, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<UhfPrimeFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waiterLock)
        {
            if (!_waiters.TryGetValue(command, out var queue))
            {
                queue = new Queue<TaskCompletionSource<UhfPrimeFrame>>();
                _waiters[command] = queue;
            }

            queue.Enqueue(tcs);
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs;
    }

    private void RemoveWaiter(ushort command, TaskCompletionSource<UhfPrimeFrame> waiter)
    {
        lock (_waiterLock)
        {
            if (_waiters.TryGetValue(command, out var queue))
            {
                var remaining = new Queue<TaskCompletionSource<UhfPrimeFrame>>(queue.Count);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!ReferenceEquals(current, waiter))
                    {
                        remaining.Enqueue(current);
                    }
                }

                if (remaining.Count > 0)
                {
                    _waiters[command] = remaining;
                }
                else
                {
                    _waiters.Remove(command);
                }
            }
        }
    }

    private async Task SendAsync(UhfPrimeCommand command, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        if (payload.Length > byte.MaxValue)
        {
            throw new ArgumentException("Payload cannot exceed 255 bytes.", nameof(payload));
        }

        var frameLength = 5 + payload.Length + 2;
        var buffer = new byte[frameLength];
        buffer[0] = FrameHead;
        buffer[1] = Address;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), (ushort)command);
        buffer[4] = (byte)payload.Length;
        if (!payload.IsEmpty)
        {
            payload.Span.CopyTo(buffer.AsSpan(5));
        }

        var crc = Crc16.Compute(buffer.AsSpan(0, frameLength - 2));
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(frameLength - 2), crc);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TraceFrame(buffer, true, Address, (ushort)command, 0x00, payload.Length, "TX");
            await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void TraceFrame(byte[] raw, bool isValid, byte address, ushort command, byte status, int payloadLength, string note)
    {
        FrameTraced?.Invoke(this, new FrameTraceEventArgs(raw, isValid, address, command, status, payloadLength, note));
        var hex = BitConverter.ToString(raw).Replace('-', ' ');
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {note} CMD=0x{command:X4} STATUS=0x{status:X2} LEN={payloadLength} Valid={isValid} HEX={hex}");
    }
}
