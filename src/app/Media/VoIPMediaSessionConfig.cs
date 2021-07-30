//-----------------------------------------------------------------------------
// Filename: VoIPMediaSessionConfig.cs
//
// Description: This class configures a VoIPMediaSession.
//
// Author(s):
// Kurt Kießling
//
// History:
// 29 Jul 2021	Kurt Kießling	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media
{
    public sealed class VoIPMediaSessionConfig
    {
        public MediaEndPoints MediaEndPoint { get; set; }
        
        public IPAddress BindAddress { get; set; }
        
        public int BindPort { get; set; }
        
        public RtpSecureMediaOptionEnum RtpSecureMediaOption { get; set; }

        public VideoTestPatternSource TestPatternSource { get; set; }
    }
}