//-----------------------------------------------------------------------------
// Filename: PaidVdieoFrameConfig.cs
//
// Description: Immutable record to control the generation of an annotated bitmap
// which in turn will be used as the source for a WebRTC video stream.
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
using SixLabors.ImageSharp;

namespace demo;

public record PaidVideoFrameConfig(
    DateTimeOffset StartTime,
    string? LightningPaymentRequest,
    int Opacity,
    Color BorderColour,
    string Title,
    bool IsPaid,
    string ImagePath,
    string QrCodeLogoPath);
