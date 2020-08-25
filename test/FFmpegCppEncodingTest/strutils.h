//-----------------------------------------------------------------------------
// Filename: strutils.h
//
// Description: Useful string utilities originally from Bitcoin Core.
//
// Copyright (c) 2009-2010 Satoshi Nakamoto
// Copyright (c) 2009-2017 The Bitcoin Core developers
// Distributed under the MIT software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.
//-----------------------------------------------------------------------------

#ifndef STRUTILS_H
#define STRUTILS_H

#include <iomanip>
#include <sstream>
#include <string>
#include <vector>

namespace
{
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
const std::string toHex(const T& begin, const T& end)
{
	std::ostringstream str;
	for (T it = begin; it != end; ++it)
		str << std::setw(2) << std::setfill('0') << std::hex << (unsigned)(*it & 0xff);

	return str.str();
}

template < class T >
const std::string toHex(const T& v)
{
	return toHex(v.begin(), v.end());
}
}

#endif STRUTILS_H