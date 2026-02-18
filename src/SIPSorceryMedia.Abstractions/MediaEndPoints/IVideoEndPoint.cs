//-----------------------------------------------------------------------------
// Filename: IVideoEndPoint.cs
//
// Description: Represents a combined video source and sink (e.g. webcam and bitmap).
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

public interface IVideoEndPoint : IVideoSource, IVideoSink
{
    /// <summary>
    /// Pauses the video source and sink. The source will stop sending frames and the sink will stop receiving them.
    /// </summary>
    Task Pause();

    /// <summary>
    /// Resumes the video source and sink. The source will start sending frames and the sink will start receiving them.
    /// </summary>
    Task Resume();

    /// <summary>
    /// Starts the video source and sink. The source will start sending frames and the sink will start receiving them.
    /// </summary>
    Task Start();

    /// <summary>
    /// Closes the video source and sink. The source will stop sending frames and the sink will stop receiving them.
    /// </summary>
    Task Close();
}
