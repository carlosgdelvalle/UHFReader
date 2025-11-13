using System;

namespace UhfPrime.Client;

public sealed class FrameTraceEventArgs : EventArgs
{
    public FrameTraceEventArgs(byte[] raw, bool isValid, byte address, ushort command, byte status, int payloadLength, string note)
    {
        Raw = raw;
        IsValid = isValid;
        Address = address;
        Command = command;
        Status = status;
        PayloadLength = payloadLength;
        Note = note;
    }

    public byte[] Raw { get; }

    public bool IsValid { get; }

    public byte Address { get; }

    public ushort Command { get; }

    public byte Status { get; }

    public int PayloadLength { get; }

    public string Note { get; }
}
