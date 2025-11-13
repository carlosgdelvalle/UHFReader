using System;
using System.Buffers.Binary;

namespace UhfPrime.Client;

public sealed record TagReport(
    string Epc,
    string Uid,
    double Rssi,
    byte Antenna,
    byte Channel,
    DateTimeOffset Timestamp,
    byte[] RawPayload)
{
    public static bool TryParseInventoryPayload(ReadOnlySpan<byte> payload, out TagReport? report)
    {
        report = null;
        if (payload.Length < 5)
        {
            return false;
        }

        var rawRssi = BinaryPrimitives.ReadInt16BigEndian(payload[..2]);
        var antenna = payload[2];
        var channel = payload[3];

        int offset;
        int epcLength;
        var lengthByte = payload[4];
        if (lengthByte > 0 && payload.Length >= 5 + lengthByte)
        {
            offset = 5;
            epcLength = lengthByte;
        }
        else if (payload.Length >= 6)
        {
            var pc = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(4, 2));
            epcLength = (int)((pc >> 11) * 2);
            offset = 6;
            if (epcLength == 0 || payload.Length < offset + epcLength)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        var epcBytes = payload.Slice(offset, epcLength).ToArray();
        var epc = Convert.ToHexString(epcBytes);
        double rssi = rawRssi / 10.0;
        report = new TagReport(epc, epc, rssi, antenna, channel, DateTimeOffset.UtcNow, payload.ToArray());
        return true;
    }
}
