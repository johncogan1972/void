using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Void.Determinism;

/// <summary>
/// Byte sink for reference payloads (VOID-008).
///
/// Every primitive is written little-endian explicitly rather than relying on
/// <see cref="BinaryWriter"/>'s documented endianness, so the payload's byte
/// layout is stated in this file and cannot drift with the BCL or the host
/// architecture. Doubles are written as their raw IEEE-754 bit pattern: no
/// formatting, no culture, no rounding.
/// </summary>
public sealed class PayloadWriter
{
    private readonly Stream _stream;
    private readonly byte[] _scratch = new byte[8];

    /// <summary>
    /// Wraps a destination stream. The writer never seeks and never buffers, so
    /// bytes land in call order; the caller owns the stream and its disposal.
    /// </summary>
    /// <param name="stream">Destination. Must be writable.</param>
    public PayloadWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    /// <summary>Writes a single byte.</summary>
    public void WriteByte(byte value) => _stream.WriteByte(value);

    /// <summary>Writes a 32-bit signed integer, little-endian.</summary>
    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_scratch, value);
        _stream.Write(_scratch, 0, 4);
    }

    /// <summary>Writes a 64-bit unsigned integer, little-endian.</summary>
    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_scratch, value);
        _stream.Write(_scratch, 0, 8);
    }

    /// <summary>Writes the raw IEEE-754 bit pattern of a double, little-endian.</summary>
    public void WriteDouble(double value) => WriteUInt64(BitConverter.DoubleToUInt64Bits(value));

    /// <summary>Writes a UTF-8 string as a little-endian byte count followed by its bytes.</summary>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(bytes.Length);
        _stream.Write(bytes, 0, bytes.Length);
    }
}
