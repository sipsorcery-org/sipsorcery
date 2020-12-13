//-----------------------------------------------------------------------------
// Filename: boolhuff.cs
//
// Description: Port of:
//  - boolhuff.h
//  - boolhuff.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 02 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------
namespace Vpx.Net
{
    public unsafe struct BOOL_CODER
    {
        public uint lowvalue;
        public uint range;
        public int count;
        public uint pos;
        public byte* buffer;
        public byte* buffer_end;
        public vpx_internal_error_info error;
    }

    /// <summary>
    /// Boolean entropy encoder.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc6386#section-7 for a description of how this
    /// encoder works.
    /// </remarks>
    public static class boolhuff
    {
        public static readonly uint[] vp8_prob_cost = {
          2047, 2047, 1791, 1641, 1535, 1452, 1385, 1328, 1279, 1235, 1196, 1161, 1129,
          1099, 1072, 1046, 1023, 1000, 979,  959,  940,  922,  905,  889,  873,  858,
          843,  829,  816,  803,  790,  778,  767,  755,  744,  733,  723,  713,  703,
          693,  684,  675,  666,  657,  649,  641,  633,  625,  617,  609,  602,  594,
          587,  580,  573,  567,  560,  553,  547,  541,  534,  528,  522,  516,  511,
          505,  499,  494,  488,  483,  477,  472,  467,  462,  457,  452,  447,  442,
          437,  433,  428,  424,  419,  415,  410,  406,  401,  397,  393,  389,  385,
          381,  377,  373,  369,  365,  361,  357,  353,  349,  346,  342,  338,  335,
          331,  328,  324,  321,  317,  314,  311,  307,  304,  301,  297,  294,  291,
          288,  285,  281,  278,  275,  272,  269,  266,  263,  260,  257,  255,  252,
          249,  246,  243,  240,  238,  235,  232,  229,  227,  224,  221,  219,  216,
          214,  211,  208,  206,  203,  201,  198,  196,  194,  191,  189,  186,  184,
          181,  179,  177,  174,  172,  170,  168,  165,  163,  161,  159,  156,  154,
          152,  150,  148,  145,  143,  141,  139,  137,  135,  133,  131,  129,  127,
          125,  123,  121,  119,  117,  115,  113,  111,  109,  107,  105,  103,  101,
          99,   97,   95,   93,   92,   90,   88,   86,   84,   82,   81,   79,   77,
          75,   73,   72,   70,   68,   66,   65,   63,   61,   60,   58,   56,   55,
          53,   51,   50,   48,   46,   45,   43,   41,   40,   38,   37,   35,   33,
          32,   30,   29,   27,   25,   24,   22,   21,   19,   18,   16,   15,   13,
          12,   10,   9,    7,    6,    4,    3,    1,    1
        };

        public unsafe static void vp8_start_encode(ref BOOL_CODER bc, byte[] source, int length)
        {
            fixed(byte* pSrc = source)
            {
                vp8_start_encode(ref bc, pSrc, pSrc + length);
            }
        }

        public unsafe static void vp8_start_encode(ref BOOL_CODER bc, byte* source, byte* source_end)
        {
            bc.lowvalue = 0;
            bc.range = 255;
            bc.count = -24;
            bc.buffer = source;
            bc.buffer_end = source_end;
            bc.pos = 0;
        }

        public static void vp8_stop_encode(ref BOOL_CODER bc)
        {
            int i;

            for (i = 0; i < 32; ++i) vp8_encode_bool(ref bc, 0, 128);
        }

        public static void vp8_encode_value(ref BOOL_CODER bc, int data, int bits)
        {
            int bit;

            for (bit = bits - 1; bit >= 0; bit--)
            {
                vp8_encode_bool(ref bc, (1 & (data >> bit)), 0x80);
            }
        }

        public unsafe static int validate_buffer(in byte* start, ulong len, in byte* end,
                           ref vpx_internal_error_info error)
        {
            if (start + len > start && start + len < end)
            {
                return 1;
            }
            else
            {
                vpx_codec.vpx_internal_error(ref error, vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME,
                                   "Truncated packet or corrupt partition ");
            }

            return 0;
        }

        public unsafe static void vp8_encode_bool(ref BOOL_CODER bc, int bit, int probability)
        {
            uint split;
            int count = bc.count;
            uint range = bc.range;
            uint lowvalue = bc.lowvalue;
            int shift;

            split = (uint)(1 + (((range - 1) * probability) >> 8));

            range = split;

            if (bit > 0)
            {
                lowvalue += split;
                range = bc.range - split;
            }

            shift = entropy.vp8_norm[range];

            range <<= shift;
            count += shift;

            if (count >= 0)
            {
                int offset = shift - count;

                if (((lowvalue << (offset - 1)) & 0x80000000) > 0)
                {
                    int x = (int)bc.pos - 1;

                    while (x >= 0 && bc.buffer[x] == 0xff)
                    {
                        bc.buffer[x] = (byte)0;
                        x--;
                    }

                    bc.buffer[x] += 1;
                }

                validate_buffer(bc.buffer + bc.pos, 1, bc.buffer_end, ref bc.error);
                bc.buffer[bc.pos++] = (byte)(lowvalue >> (24 - offset) & 0xff);

                lowvalue <<= offset;
                shift = count;
                lowvalue &= 0xffffff;
                count -= 8;
            }

            lowvalue <<= shift;
            bc.count = count;
            bc.lowvalue = lowvalue;
            bc.range = range;
        }
    }
}
