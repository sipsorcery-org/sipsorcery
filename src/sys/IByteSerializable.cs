using System;

namespace SIPSorcery.Sys;

/// <summary>
/// Provides a lightweight contract for types that can report the exact number of bytes
/// they require for serialisation and write their serialised representation into a caller
/// supplied <see cref="Span{T}"/>. Implementations MUST write exactly <see cref="GetByteCount"/>
/// bytes starting at index 0 of the supplied buffer.
/// </summary>
public interface IByteSerializable
{
    /// <summary>
    /// Gets the exact number of bytes required to serialise this instance.
    /// </summary>
    /// <returns>Total byte count of the serialised form.</returns>
    int GetByteCount();

    /// <summary>
    /// Writes the serialised representation into <paramref name="buffer"/>.
    /// Implementations MUST: (1) ensure <paramref name="buffer"/> has a length of at least
    /// <see cref="GetByteCount"/>, (2) write exactly that many bytes starting at index 0,
    /// and (3) return the number of bytes written (typically the same value as <see cref="GetByteCount"/>).
    /// </summary>
    /// <param name="buffer">Destination span to receive the serialised bytes.</param>
    /// <returns>The number of bytes written.</returns>
    int WriteBytes(Span<byte> buffer);
}
