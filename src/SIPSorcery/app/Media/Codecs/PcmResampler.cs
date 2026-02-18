using System;
using System.Linq;

namespace SIPSorcery.Media
{
    internal static class PcmResampler
    {
        public static short[] Resample(short[] pcm, int inRate, int outRate)
        {
            if (inRate == outRate)
            {
                return pcm;
            }
            else if (inRate == 8000 && outRate == 16000)
            {
                // Crude up-sample to 16Khz by doubling each sample.
                return pcm.SelectMany(x => new short[] { x, x }).ToArray();
            }
            else if (inRate == 8000 && outRate == 48000)
            {
                // Crude up-sample to 48Khz by 6x each sample. This sounds bad, use for testing only.
                return pcm.SelectMany(x => new short[] { x, x, x, x, x, x }).ToArray();
            }
            else if (inRate == 16000 && outRate == 8000)
            {
                // Crude down-sample to 8Khz by skipping every second sample.
                return pcm.Where((x, i) => i % 2 == 0).ToArray();
            }
            else if (inRate == 16000 && outRate == 48000)
            {
                // Crude up-sample to 48Khz by 3x each sample. This sounds bad, use for testing only.
                return pcm.SelectMany(x => new short[] { x, x, x }).ToArray();
            }
            else
            {
                throw new ApplicationException($"Sorry don't know how to re-sample PCM from {inRate} to {outRate}. Pull requests welcome!");
            }
        }
    }
}
