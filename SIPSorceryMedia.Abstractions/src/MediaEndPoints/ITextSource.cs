﻿//-----------------------------------------------------------------------------
// Filename: ITextSource.cs
//
// Description: Interface to represent a text source or capture device,
// such as a fax machine.
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

using System;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions;

public interface ITextSource
{
    event Action<byte[]> OnTextSourceEncodedSample;
    Task CloseText();
    TextFormat GetTextSourceFormat();
    void SetTextSourceFormat(TextFormat textFormat);
    Task StartText();
    Task PauseText();
    Task ResumeText();
}
