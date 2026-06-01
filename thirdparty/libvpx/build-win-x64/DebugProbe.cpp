#include "DebugProbe.h"
#include "Windows.h"

#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

const signed char p_util_hexdigit[256] =
{ -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
0, 1, 2, 3, 4, 5, 6, 7, 8, 9, -1, -1, -1, -1, -1, -1,
-1, 0xa, 0xb, 0xc, 0xd, 0xe, 0xf, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, 0xa, 0xb, 0xc, 0xd, 0xe, 0xf, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, };

signed char HexDigit(char c)
{
	return p_util_hexdigit[(unsigned char)c];
}

bool IsHex(const std::string& str)
{
	for (std::string::const_iterator it(str.begin()); it != str.end(); ++it)
	{
		if (HexDigit(*it) < 0)
			return false;
	}
	return (str.size() > 0) && (str.size() % 2 == 0);
}

bool IsHexNumber(const std::string& str)
{
	size_t starting_location = 0;
	if (str.size() > 2 && *str.begin() == '0' && *(str.begin() + 1) == 'x') {
		starting_location = 2;
	}
	for (auto c : str.substr(starting_location)) {
		if (HexDigit(c) < 0) return false;
	}
	// Return false for empty string or "0x".
	return (str.size() > starting_location);
}

std::vector<uint8_t> ParseHex(const char* psz)
{
	// convert hex dump to vector
	std::vector<unsigned char> vch;
	while (true)
	{
		while (isspace(*psz))
			psz++;
		signed char c = HexDigit(*psz++);
		if (c == (signed char)-1)
			break;
		unsigned char n = (c << 4);
		c = HexDigit(*psz++);
		if (c == (signed char)-1)
			break;
		n |= c;
		vch.push_back(n);
	}
	return vch;
}

std::vector<uint8_t> ParseHex(const std::string& str)
{
	return ParseHex(str.c_str());
}

template < class T >
const std::string toHexStr(const T& begin, const T& end)
{
	std::ostringstream str;
	for (T it = begin; it != end; ++it)
		str << std::setw(2) << std::setfill('0') << std::hex << (unsigned)(*it & 0xff);

	return str.str();
}

template < class T >
const std::string toHex(const T& v)
{
	return toHexStr(v.begin(), v.end());
}

std::string GetBModeInfoMatrix(b_mode_info * bModes)
{
	// The array will always be 16 elements.
	std::string matrixStr;

	for (int row = 0; row < 4; row++)
	{
		matrixStr += "[" + std::to_string(bModes[row * 4].mv.as_int) +
			"," + std::to_string(bModes[row * 4 + 1].mv.as_int) +
			"," + std::to_string(bModes[row * 4  +2].mv.as_int) + 
			"," + std::to_string(bModes[row * 4 + 3].mv.as_int) + "]\n";
	}

	return matrixStr + "\n";
}

void dump_motion_vectors(MODE_INFO* mip, int macroBlockCols, int macroBlockRows)
{
	// vpx_calloc((oci->mb_cols + 1) * (oci->mb_rows + 1), sizeof(MODE_INFO));
	OutputDebugStringA("dump_motion_vectors\n");
	OutputDebugStringA("Macro Block Modes:\n");
	for (int i = 0; i < macroBlockRows + 1; i++)
	{
		std::string rowStr = std::to_string(i) + " | ";
		for (int j = 0; j < macroBlockCols + 1; j++)
		{
			uint8_t yMode = mip[i * (macroBlockRows + 1) + j].mbmi.mode;
			uint8_t uvMode = mip[i * (macroBlockRows + 1) + j].mbmi.uv_mode;
			rowStr += "y=" + std::to_string(yMode) + ", uvMode=" + std::to_string(uvMode) + " | ";
		}
		rowStr += "\n";
		OutputDebugStringA(rowStr.c_str());
	}

	OutputDebugStringA("\nSub-Block Prediction Modes:\n");
	for (int i = 0; i < macroBlockRows + 1; i++)
	{
		for (int j = 0; j < macroBlockCols + 1; j++)
		{
			std::string hdr = "[" + std::to_string(i) + "," + std::to_string(j) + "]\n";
			OutputDebugStringA(hdr.c_str());
			OutputDebugStringA(GetBModeInfoMatrix(mip[i * (macroBlockRows + 1) + j].bmi).c_str());
		}
	}
}

void dump_macro_block(MACROBLOCKD* xd, int mb_idx)
{
	std::string description = "MacroBlock " + std::to_string(mb_idx) + ":\n";
	OutputDebugStringA(description.c_str());

	OutputDebugStringA("eobs: ");
	for (int i = 0; i < 25; i++) {
		std::string e = std::to_string(xd->eobs[i]) + ", ";
		OutputDebugStringA(e.c_str());
	}
	OutputDebugStringA("\n");
 
	std::string yHex = toHexStr(xd->dst.y_buffer, xd->dst.y_buffer + xd->dst.y_width);
	std::string uHex = toHexStr(xd->dst.u_buffer, xd->dst.u_buffer + xd->dst.uv_width);
	std::string vHex = toHexStr(xd->dst.v_buffer, xd->dst.v_buffer + xd->dst.uv_width);

	OutputDebugStringA(std::string("y: " + yHex + "\n").c_str());
	OutputDebugStringA(std::string("u: " + uHex + "\n").c_str());
	OutputDebugStringA(std::string("v: " + vHex + "\n").c_str());

	OutputDebugStringA("\n");
}

void dump_subblock_coefficients(MACROBLOCKD* xd)
{
	OutputDebugStringA("MacroBlock subblock qcoeff:\n");

	for (int i = 0; i < 25; i++)
	{
		BLOCKD subBlock = xd->block[i];
		std::string qCoeffStr = "block[" + std::to_string(i) + "].qcoeff=";
		short* qcoeff = subBlock.qcoeff;
		for (int j = 0; j < 16; j++)
		{
			qCoeffStr += std::to_string(*qcoeff) + ",";
			qcoeff++;
		}
		OutputDebugStringA(qCoeffStr.c_str());
		OutputDebugStringA("\n");
	}

	OutputDebugStringA("\n");

	OutputDebugStringA("MacroBlock subblock dqcoeff:\n");

	for (int i = 0; i < 25; i++)
	{
		BLOCKD subBlock = xd->block[i];
		std::string dqCoeffStr = "block[" + std::to_string(i) + "].dqcoeff=";
		short* dqcoeff = subBlock.dqcoeff;
		for (int j = 0; j < 16; j++)
		{
			dqCoeffStr += std::to_string(*dqcoeff) + ",";
			dqcoeff++;
		}
		OutputDebugStringA(dqCoeffStr.c_str());
		OutputDebugStringA("\n");
	}

	OutputDebugStringA("\n");
}

void dump_ysubblock(int i, uint8_t* dst, int dst_stride)
{
	std::string ysub = "y[" + std::to_string(i) + "]:" + toHexStr(dst, dst + dst_stride) + "\n";
	OutputDebugStringA(ysub.c_str());
}

void dump_above_and_left(uint8_t* above, uint8_t* left)
{
	auto aboveHex = toHexStr(above + 3, above + 12);
	auto leftHex = toHexStr(left, left + 4);

	std::string dump = "above=" + aboveHex + ",left=" + leftHex + "\n";
	OutputDebugStringA(dump.c_str());
}
