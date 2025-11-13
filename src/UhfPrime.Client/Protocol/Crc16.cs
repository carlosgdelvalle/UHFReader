using System;

namespace UhfPrime.Client.Protocol;

internal static class Crc16
{
    private const ushort Polynomial = 0x8408;

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
            {
                var lsb = (crc & 0x0001) != 0;
                crc >>= 1;
                if (lsb)
                {
                    crc ^= Polynomial;
                }
            }
        }

        return crc;
    }
}
