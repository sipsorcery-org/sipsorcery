//-----------------------------------------------------------------------------
// Filename: ITextSink.cs
//
// Description: Interface to represent a text sink..
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 May 2025  Aaron Clauson   Refactored from MediaEndPoints.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions;

public interface ITextSink
{
    Task CloseTextSink();
    void GotTextRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, int marker, byte[] payload);
    void SetTextSinkFormat(TextFormat textFormat);
    TextFormat GetTextSinkFormat();
    Task StartTextSink();
    Task PauseTextSink();
    Task ResumeTextSink();
}
