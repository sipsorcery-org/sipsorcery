using System;

namespace SIPSorcery.Media
{
    public static class PcmResampler
    {
        /// <summary>
        /// Resamples PCM using linear interpolation. Not as good as a proper windowed-sinc
        /// filter but a big improvement over sample repetition, which mirrors the source
        /// spectrum at multiples of the input rate. Those images are inaudible through a
        /// narrowband codec (G711 truncates at 4KHz) but are faithfully transmitted by
        /// wideband codecs such as OPUS and make upsampled audio sound harsh and metallic.
        /// </summary>
        public static short[] Resample(short[] pcm, int inRate, int outRate)
        {
            if (inRate == outRate || pcm.Length == 0)
            {
                return pcm;
            }

            int outLength = (int)((long)pcm.Length * outRate / inRate);
            var resampled = new short[outLength];
            double step = (double)inRate / outRate;

            for (int i = 0; i < outLength; i++)
            {
                double srcPos = i * step;
                int srcIndex = (int)srcPos;
                double frac = srcPos - srcIndex;

                short s0 = pcm[srcIndex];
                short s1 = pcm[Math.Min(srcIndex + 1, pcm.Length - 1)];

                resampled[i] = (short)Math.Round(s0 + (s1 - s0) * frac);
            }

            return resampled;
        }
    }
}
