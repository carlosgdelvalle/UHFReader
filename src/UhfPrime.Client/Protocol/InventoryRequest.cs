using System;
using System.Buffers.Binary;

namespace UhfPrime.Client.Protocol;

/// <summary>
/// Inventory request mode and associated parameter (time or cycle count).
/// </summary>
public readonly record struct InventoryRequest(byte Mode, uint Parameter)
{
    public static InventoryRequest Continuous => new(0x00, 0);

    public byte[] ToPayload()
    {
        Span<byte> buffer = stackalloc byte[5];
        buffer[0] = Mode;
        BinaryPrimitives.WriteUInt32BigEndian(buffer[1..], Parameter);
        return buffer.ToArray();
    }
}
