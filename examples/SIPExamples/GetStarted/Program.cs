//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPSorcery
// library to place a call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
// 31 Dec 2019  Aaron Clauson   Changed from an OPTIONS example to a call example.
// 20 Feb 2020  Aaron Clauson   Switched to RtpAVSession and simplified.
// 02 Feb 2021  Aaron Clauson   Removed logging to make main logic more obvious.
// 14 Jan 2024  Aaron Clauson   Updated target from netcoreapp3.1 to net8.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;
using SIPSorceryMedia.Windows;
using SIPSorceryMedia.Abstractions;

const string DESTINATION = "music@iptel.org";
Console.WriteLine("SIP Get Started");

var userAgent = new SIPUserAgent();
var winAudio = new WindowsAudioEndPoint(new AudioEncoder());
winAudio.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMU);
var voipMediaSession = new VoIPMediaSession(winAudio.ToMediaEndPoints());
voipMediaSession.AcceptRtpFromAny = true;

// Place the call and wait for the result.
bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);
Console.WriteLine($"Call result {(callResult ? "success" : "failure")}.");

Console.WriteLine("Press any key to hangup and exit.");
Console.ReadLine();
