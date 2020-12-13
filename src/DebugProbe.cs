//-----------------------------------------------------------------------------
// Filename: DebugProbe.cs
//
// Description: This class is used to dump particular parts of the codec state.
// The reason to use this approach is to allow comparisons with the C VP8
// implementation. The C implementation does not lend itself to testing
// to testing individual functions in the encode or decode pipeline. Consequently
// dumping the internal state at specific points is the chosen way to compare
// the C and C# versions.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Diagnostics;

namespace Vpx.Net
{
    public static class DebugProbe
    {
		public static void DumpMotionVectors(MODE_INFO[] mip, int macroBlockCols, int macroBlockRows)
		{
			Debug.WriteLine("DumpMotionVectors:");
			Debug.WriteLine("Macro Block Modes:");
			for (int i = 0; i < macroBlockRows + 1; i++)
			{
				string rowStr = $"Row {i} | ";
				for (int j = 0; j < macroBlockCols + 1; j++)
				{
					byte yMode = mip[i * (macroBlockRows + 1) + j].mbmi.mode;
					byte uvMode = mip[i * (macroBlockRows + 1) + j].mbmi.uv_mode;
					rowStr += $"y={yMode}, uvMode={uvMode} | ";
				}
				Debug.WriteLine(rowStr);
			}

			Debug.WriteLine("\nSub-Block Prediction Modes:");
			for (int i = 0; i < macroBlockRows + 1; i++)
			{
				for (int j = 0; j < macroBlockCols + 1; j++)
				{
					Debug.WriteLine($"[{i},{j}]:");
					Debug.WriteLine(GetBModeInfoMatrix(mip[i * (macroBlockRows + 1) + j].bmi));
				}
			}
		}

		public static string GetBModeInfoMatrix(b_mode_info[] bModes)
        {
			// The array will always be 16 elements.
			string matrixStr = null;

			for(int row=0; row<4; row++)
            {
				matrixStr += $"[{bModes[row * 4].mv.as_int},{bModes[row * 4 + 1].mv.as_int}" +
							 $",{bModes[row * 4  +2].mv.as_int},{bModes[row * 4 + 3].mv.as_int}]\n"; 
			}

			return matrixStr;
        }

		public static unsafe void DumpMacroBlock(MACROBLOCKD macroBlock, int macroBlockIndex)
        {
            Debug.WriteLine($"MacroBlock {macroBlockIndex}:");

            Debug.Write("eobs: ");
            for(int i=0; i< macroBlock.eobs.Length; i++)
            {
                Debug.Write($"{macroBlock.eobs[i]}, ");
            }
            Debug.WriteLine("");

            Debug.WriteLine($"y: {DebugProbeHexStr.ToHexStr(macroBlock.dst.y_buffer, macroBlock.dst.y_width)}");
            Debug.WriteLine($"u: {DebugProbeHexStr.ToHexStr(macroBlock.dst.u_buffer, macroBlock.dst.uv_width)}");
            Debug.WriteLine($"v: {DebugProbeHexStr.ToHexStr(macroBlock.dst.v_buffer, macroBlock.dst.uv_width)}");
            Debug.WriteLine("");
        }

        public static unsafe void DumpSubBlockCoefficients(MACROBLOCKD macroBlock)
        {
            Debug.WriteLine($"MacroBlock subblock qcoeff:");

            for(int i=0; i< macroBlock.block.Length; i++)
            {
                var subBlock = macroBlock.block[i];
                string qCoeff = null;
                for(int j=subBlock.qcoeff.Index; j< subBlock.qcoeff.Index+16; j++)
                {
                    qCoeff += subBlock.qcoeff.src()[j].ToString() + ",";
                }
                Debug.WriteLine($"block[{i}].qcoeff={qCoeff}");
            }

            Debug.WriteLine("");

            Debug.WriteLine($"MacroBlock subblock dqcoeff:");

            for (int i = 0; i < macroBlock.block.Length; i++)
            {
                var subBlock = macroBlock.block[i];
                string dqCoeff = null;
                for (int j = subBlock.dqcoeff.Index; j < subBlock.dqcoeff.Index + 16; j++)
                {
                    dqCoeff += subBlock.dqcoeff.src()[j].ToString() + ",";
                }
                Debug.WriteLine($"block[{i}].dqcoeff={dqCoeff}");
            }

            Debug.WriteLine("");
        }

        public static unsafe void DumpYSubBlock(int index, byte* dst, int stride)
        {
            Debug.WriteLine($"y[{index}]: {DebugProbeHexStr.ToHexStr(dst, stride)}");
        }

        public static unsafe void DumpAboveAndLeft(byte[] above, byte[] left)
        {
            string aboveHex = null;
            fixed(byte* pAbove = above)
            {
                aboveHex = DebugProbeHexStr.ToHexStr(pAbove + 3, 9);
            }
            string leftHex = null;
            fixed (byte* pLeft = left)
            {
                leftHex = DebugProbeHexStr.ToHexStr(pLeft, 4);
            }
            Debug.WriteLine($"above={aboveHex},left={leftHex}");
        }
    }

    public static class DebugProbeHexStr
    {
        private static readonly sbyte[] _hexDigits =
            { -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              0,1,2,3,4,5,6,7,8,9,-1,-1,-1,-1,-1,-1,
              -1,0xa,0xb,0xc,0xd,0xe,0xf,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,0xa,0xb,0xc,0xd,0xe,0xf,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, };

        private static readonly char[] hexmap = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        public unsafe static string ToHexStr(byte* buffer, int length, char? separator = null)
        {
            string rv = string.Empty;

            for (int i = 0; i < length; i++)
            {
                var val = buffer[i];
                rv += hexmap[val >> 4];
                rv += hexmap[val & 15];

                if (separator != null && i != length - 1)
                {
                    rv += separator;
                }
            }

            return rv.ToLower();
        }
    }
}
