//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program to send text-to-speech requests to the 
// Azure Cognitive Speech Services API and save the results to PCM files.
//
// Update 4 Feb 2025: You need an "Azure AI services | Speech service" resource
// for this demo. Once the resource is set up, you can get the subscription key
// and region from the Overiew page on the Azure portal.
//
// References:
// https://learn.microsoft.com/en-us/azure/ai-services/speech-service/overview
//
// Notes:
// The audio format returned by the Azure Speech Service is:
// 16 bit signed PCM at 16KHz
//
// To convert to mp3 format using ffmpeg use:
// ffmpeg -f s16le -ar 16k -i 637267813207431881.pcm16k out.mp3
//
// To convert to a PCM format suitable for the PlaySounds SIP example
// program use:
//ffmpeg -f s16le -ar 16k -ac 1 -i 637267813207431881.pcm16k -f s16le -ac 1 -ar 8k out.raw
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 03 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
// 04 Feb 2025  Aaron Clauson   Checked still working with net9.0.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace demo;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Text to PCM Console:");

        if (args == null || args.Length < 3)
        {
            Console.WriteLine("Usage: texttopcm <azure subscription key> <azure region> <text>");
            Console.WriteLine("e.g. texttopcm cb965... westeurope  \"Hello World\"");
            Console.WriteLine("e.g. dotnet run -- cb965... westeurope  \"Hello World\"");
        }
        else
        {
            var speechConfig = SpeechConfig.FromSubscription(args[0], args[1]);

            string text = args[2];

            TextToSpeechStream ttsOutStream = new TextToSpeechStream();
            AudioConfig audioTtsConfig = AudioConfig.FromStreamOutput(ttsOutStream);
            SpeechSynthesizer speechSynthesizer = new SpeechSynthesizer(speechConfig, audioTtsConfig);

            using (var result = await speechSynthesizer.SpeakTextAsync(text))
            {
                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    Console.WriteLine($"Speech synthesized to speaker for text [{text}]");

                    var buffer = ttsOutStream.GetPcmBuffer();
                    string saveFilename = DateTime.Now.Ticks.ToString() + ".pcm16k";

                    using (StreamWriter sw = new StreamWriter(saveFilename))
                    {
                        for(int i=0; i<buffer.Length; i++)
                        {
                            sw.BaseStream.Write(BitConverter.GetBytes(buffer[i]));
                        }
                    }

                    Console.WriteLine($"Result saved to {saveFilename}.");
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                    Console.WriteLine($"Speech synthesizer failed was cancelled, reason={cancellation.Reason}");

                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"Speech synthesizer cancelled: ErrorCode={cancellation.ErrorCode}");
                        Console.WriteLine($"Speech synthesizer cancelled: ErrorDetails=[{cancellation.ErrorDetails}]");
                    }
                }
                else
                {
                    Console.WriteLine($"Speech synthesizer failed with result {result.Reason} for text [{text}].");
                }
            }
        }
    }
}
