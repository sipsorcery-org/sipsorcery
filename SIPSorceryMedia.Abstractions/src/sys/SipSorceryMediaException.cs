using System;
using System.Diagnostics.CodeAnalysis;

namespace SIPSorceryMedia
{
    /// <summary>
    /// Represents errors that occur during media processing in the SIP Sorcery media library.
    /// </summary>
    /// <remarks>
    /// Use this exception to indicate failures related to media operations, such as audio or video
    /// processing errors, within the SIP Sorcery framework. This exception can be caught to handle media-specific error
    /// conditions separately from other exception types.
    /// </remarks>
    public class SipSorceryMediaException : ApplicationException
    {
        /// <summary>Initializes a new instance of the <see cref="SipSorceryMediaException" /> class.</summary>
        public SipSorceryMediaException()
        {
        }

        /// <summary>Initializes a new instance of the <see cref="SipSorceryMediaException" /> class with a specified error message.</summary>
        /// <param name="message">The message that describes the error.</param>
        public SipSorceryMediaException(string? message) : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="SipSorceryMediaException" /> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (<see langword="Nothing" /> in Visual Basic) if no inner exception is specified.</param>
        public SipSorceryMediaException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
