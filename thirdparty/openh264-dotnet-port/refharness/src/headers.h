// headers.h: the D1 header-dump oracle, see headers.cpp.
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

// SHA-1 of a string, lower-case hex.
std::string Sha1Hex (const std::string& s);

#endif
