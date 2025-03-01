//-----------------------------------------------------------------------------
// Filename: FrameConfig.cs
//
// Description: Immutable record to control the generation of an annotated Bitmap.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 25 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;

namespace demo;

public record FrameConfig(
    DateTimeOffset StartTime,
    string? LightningPaymentRequest,
    int Opacity,
    Color BorderColour,
    string Title,
    bool IsPaid,
    string ImagePath);
