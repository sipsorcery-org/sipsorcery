using System;
using System.Buffers;

namespace SIPSorcery.Sys;

/// <summary>
/// Provides a lightweight contract for types that can report the exact number of bytes
/// they require for serialisation and write their serialised representation using an
/// <see cref="IBufferWriter{T}"/>. Implementations MUST request exactly <see cref="GetByteCount"/>
/// bytes from the writer to avoid incremental buffer growth, write to the span, and advance the writer.
/// </summary>
public interface IByteSerializable
{
    /// <summary>
    /// Gets the exact number of bytes required to serialise this instance.
    /// </summary>
    /// <returns>Total byte count of the serialised form.</returns>
    int GetByteCount();

    /// <summary>
    /// Writes the serialised representation using <paramref name="writer"/>.
    /// Implementations MUST: (1) request a span of at least <see cref="GetByteCount"/> bytes
    /// from the writer, (2) write exactly that many bytes, (3) advance the writer by the number
    /// of bytes written, and (4) return the number of bytes written (typically the same value as <see cref="GetByteCount"/>).
    /// </summary>
    /// <param name="writer">The buffer writer to receive the serialised bytes.</param>
    /// <returns>The number of bytes written.</returns>
    int WriteTo(IBufferWriter<byte> writer);
}
