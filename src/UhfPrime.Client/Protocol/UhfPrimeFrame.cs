using System;

namespace UhfPrime.Client.Protocol;

/// <summary>
/// Represents a parsed frame returned by the reader.
/// </summary>
public sealed class UhfPrimeFrame
{
    public UhfPrimeFrame(byte address, ushort command, byte status, byte[] payload, byte[] raw)
    {
        Address = address;
        Command = command;
        Status = status;
        Payload = payload;
        Raw = raw;
    }

    public byte Address { get; }

    public ushort Command { get; }

    /// <summary>
    /// Status byte defined in Appendix C of the protocol manual.
    /// </summary>
    public byte Status { get; }

    /// <summary>
    /// Payload without the status byte.
    /// </summary>
    public byte[] Payload { get; }

    /// <summary>
    /// Raw frame including header and CRC, useful for diagnostics.
    /// </summary>
    public byte[] Raw { get; }

    public bool IsSuccess => Status == 0x00;

    public override string ToString() => $"CMD=0x{Command:X4} STATUS=0x{Status:X2} LEN={Payload?.Length ?? 0}";
}
