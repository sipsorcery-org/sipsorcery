//-----------------------------------------------------------------------------
// Filename: ITextEndPoint.cs
//
// Description: Represents a combined text source and sink.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 May 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions;

public interface ITextEndPoint : ITextSource, ITextSink
{
    /// <summary>
    /// Pauses the text source and sink. The source will stop sending samples and the sink will stop receiving them.
    /// </summary>
    Task Pause();

    /// <summary>
    /// Resumes the text source and sink. The source will start sending samples and the sink will start receiving them.
    /// </summary>
    Task Resume();

    /// <summary>
    /// Starts the text source and sink. The source will start sending samples and the sink will start receiving them.
    /// </summary>
    Task Start();

    /// <summary>
    /// Closes the text source and sink. The source will stop sending samples and the sink will stop receiving them.
    /// </summary>
    Task Close();
}
