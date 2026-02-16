using System;
using System.Diagnostics.CodeAnalysis;

namespace SIPSorcery;

/// <summary>
/// Represents errors that occur during SIP Sorcery operations.
/// </summary>
/// <remarks>
/// This exception is thrown to indicate failures specific to SIP Sorcery functionality. Use this type to
/// catch and handle errors related to SIP Sorcery components separately from other exceptions. For more information
/// about the error, inspect the exception's message and inner exception properties.
/// </remarks>
public class SipSorceryException : ApplicationException
{
    /// <summary>Initializes a new instance of the <see cref="SipSorceryException" /> class.</summary>
    public SipSorceryException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SipSorceryException" /> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public SipSorceryException(string? message) : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SipSorceryException" /> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (<see langword="Nothing" /> in Visual Basic) if no inner exception is specified.</param>
    public SipSorceryException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
