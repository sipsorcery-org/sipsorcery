﻿using System;

namespace SIPSorceryMedia
{
    public class SipSorceryMediaException : Exception
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
