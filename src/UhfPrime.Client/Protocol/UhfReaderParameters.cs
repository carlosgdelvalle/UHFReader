using System;
using System.Linq;

namespace UhfPrime.Client.Protocol;

/// <summary>
/// Represents the 25-byte payload used by RFM_SET_ALL_PARAM / RFM_GET_ALL_PARAM.
/// </summary>
public sealed class UhfReaderParameters
{
    public const int PayloadLength = 25;

    public byte Address { get; init; }
    public byte RfidProtocol { get; init; }
    public byte WorkMode { get; init; }
    public byte Interface { get; init; }
    public byte BaudRate { get; init; }
    public byte WiegandSetting { get; init; }
    public byte AntennaMask { get; init; }
    public byte[] RfidFrequency { get; init; } = new byte[8];
    public byte RfidPower { get; init; }
    public byte InquiryArea { get; init; }
    public byte QValue { get; init; }
    public byte Session { get; init; }
    public byte AccessAddress { get; init; }
    public byte AccessDataLength { get; init; }
    public byte FilterTimeSeconds { get; init; }
    public byte TriggerTimeSeconds { get; init; }
    public byte BuzzerTimeTicks { get; init; }
    public byte PollingInterval { get; init; }

    public static UhfReaderParameters FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < PayloadLength)
        {
            throw new ArgumentException($"Expected {PayloadLength} bytes but received {payload.Length}.", nameof(payload));
        }

        return new UhfReaderParameters
        {
            Address = payload[0],
            RfidProtocol = payload[1],
            WorkMode = payload[2],
            Interface = payload[3],
            BaudRate = payload[4],
            WiegandSetting = payload[5],
            AntennaMask = payload[6],
            RfidFrequency = payload.Slice(7, 8).ToArray(),
            RfidPower = payload[15],
            InquiryArea = payload[16],
            QValue = payload[17],
            Session = payload[18],
            AccessAddress = payload[19],
            AccessDataLength = payload[20],
            FilterTimeSeconds = payload[21],
            TriggerTimeSeconds = payload[22],
            BuzzerTimeTicks = payload[23],
            PollingInterval = payload[24]
        };
    }

    public byte[] ToPayload()
    {
        var payload = new byte[PayloadLength];
        payload[0] = Address;
        payload[1] = RfidProtocol;
        payload[2] = WorkMode;
        payload[3] = Interface;
        payload[4] = BaudRate;
        payload[5] = WiegandSetting;
        payload[6] = AntennaMask;
        if (RfidFrequency.Length != 8)
        {
            throw new InvalidOperationException("RfidFrequency must contain exactly 8 bytes.");
        }

        Array.Copy(RfidFrequency, 0, payload, 7, 8);
        payload[15] = RfidPower;
        payload[16] = InquiryArea;
        payload[17] = QValue;
        payload[18] = Session;
        payload[19] = AccessAddress;
        payload[20] = AccessDataLength;
        payload[21] = FilterTimeSeconds;
        payload[22] = TriggerTimeSeconds;
        payload[23] = BuzzerTimeTicks;
        payload[24] = PollingInterval;
        return payload;
    }
}
