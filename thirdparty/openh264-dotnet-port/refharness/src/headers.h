// headers.h: the D1 header-dump and D2 macroblock-dump oracles, see headers.cpp.
#ifndef REFHARNESS_HEADERS_H
#define REFHARNESS_HEADERS_H

#include <cstdint>
#include <string>
#include <vector>

typedef std::vector<std::vector<uint8_t> > Chunks;

// BaseDecoderTest::ReadFrame's split of a stream into DecodeFrame2 inputs.
Chunks SplitChunks (const std::vector<uint8_t>& stream);

// A damaged copy of the chunks (drops, truncations, bit flips), see headers.cpp.
Chunks DamageChunks (const Chunks& chunks, uint32_t streamSize, int variant);

// Runs the header-parsing driver over the chunks. On success fills out with
// the dump text and returns the NAL and access unit counts.
bool HeaderDumpChunks (const Chunks& chunks, std::string& out, int& nals, int& aus);

// HeaderDumpChunks (SplitChunks (stream), ...).
bool HeaderDump (const std::vector<uint8_t>& stream, std::string& out, int& nals, int& aus);

// The D2 macroblock dump: the same driver, also parsing each access unit's
// slices (see headers.cpp). Fills out with the dump text and returns the
// number of slices parsed and macroblock records written.
bool MbDumpChunks (const Chunks& chunks, std::string& out, int& slices, int& mbs);

// MbDumpChunks (SplitChunks (stream), ...).
bool MbDump (const std::vector<uint8_t>& stream, std::string& out, int& slices, int& mbs);

// SHA-1 of a string, lower-case hex.
std::string Sha1Hex (const std::string& s);

#endif
