//-----------------------------------------------------------------------------
// Filename: TextToSpeechStream.cs
//
// Description: This is the backing class for the Azure text-to-speech service call. 
// It will have the results of the text-to-speech request pushed into its stream 
// buffer which can be retrieved for subsequent operations such as sending via RTP.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 10 May 2020 Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using Microsoft.CognitiveServices.Speech.Audio;

namespace demo;

/// <summary>
/// This is the backing class for the Azure text-to-speech service call. It will
/// have the results of the text-to-speech request pushed into its stream buffer which
/// can be retrieved for subsequent operations such as sending via RTP.
/// </summary>
public class TextToSpeechStream : PushAudioOutputStreamCallback
{
    public MemoryStream _ms = new MemoryStream();
    private int _posn = 0;

    public TextToSpeechStream()
    { }

    /// <summary>
    /// This gets called by the internals of the Azure text-to-speech SDK to write the resultant
    /// PCM 16Khz 16 bit audio samples.
    /// </summary>
    /// <param name="dataBuffer">The data buffer containing the audio sample.</param>
    /// <returns>The number of bytes written from the supplied sample.</returns>
    public override uint Write(byte[] dataBuffer)
    {
        //Console.WriteLine($"TextToSpeechAudioOutStream bytes written to output stream {dataBuffer.Length}.");

        _ms.Write(dataBuffer, 0, dataBuffer.Length);
        _posn = _posn + dataBuffer.Length;

        return (uint)dataBuffer.Length;
    }

    /// <summary>
    /// Closes the stream.
    /// </summary>
    public override void Close()
    {
        _ms.Close();
        base.Close();
    }

    /// <summary>
    /// Get the current contents of the memory stream as a buffer of PCM samples.
    /// The PCM samples are suitable to be fed into an audio codec as part of the 
    /// RTP send.
    /// </summary>
    public short[] GetPcmBuffer()
    {
        _ms.Position = 0;
        byte[] buffer = _ms.GetBuffer();
        short[] pcmBuffer = new short[_posn / 2];

        for (int i = 0; i < pcmBuffer.Length; i++)
        {
            pcmBuffer[i] = BitConverter.ToInt16(buffer, i * 2);
        }

        return pcmBuffer;
    }

    /// <summary>
    /// Clear is intended to be called after the method to get the PCM buffer.
    /// It will reset the underlying memory buffer ready for the next text-to-speech operation.
    /// </summary>
    public void Clear()
    {
        _ms.SetLength(0);
        _posn = 0;
    }

    /// <summary>
    /// Used to check if there is data waiting to be copied.
    /// </summary>
    /// <returns>True if the stream is empty. False if there is some data available.</returns>
    public bool IsEmpty()
    {
        return _posn == 0;
    }
}
