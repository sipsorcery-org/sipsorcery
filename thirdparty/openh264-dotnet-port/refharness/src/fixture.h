// fixture.h: shared pieces for the kernel fixtures that h264ref writes.
//
// Every kernel fixture is one TSV row: group, kernel, case, sha1. The inputs
// are not stored. Both sides regenerate them from the case string with the
// same xorshift32 generator, so the C# test (test/SIPSorcery.H264.UnitTest,
// KernelFixtures.cs) must keep Rng and the buffer layouts below in step with
// this file. The SHA1 covers each kernel's whole output buffer, guard bytes
// included, so a write outside the expected region fails too.

#ifndef H264REF_FIXTURE_H
#define H264REF_FIXTURE_H

#include <cstdint>
#include <cstdio>
#include <string>

#include "sha1.h"

namespace fixture {

// xorshift32 (Marsaglia 2003). The seed is mixed so that small seeds differ.
struct Rng {
  uint32_t s;
  explicit Rng (uint32_t seed) : s (seed ^ 0xA5A5A5A5u) {
    if (s == 0) s = 1;
  }
  uint32_t Next() {
    s ^= s << 13;
    s ^= s >> 17;
    s ^= s << 5;
    return s;
  }
  uint8_t NextByte() { return (uint8_t) (Next() >> 24); }
  void Fill (uint8_t* p, size_t n) {
    for (size_t i = 0; i < n; ++i) p[i] = NextByte();
  }
};

struct Hasher {
  SHA1Context ctx;
  Hasher() { SHA1Reset (&ctx); }
  void Add (const void* p, size_t n) { SHA1Input (&ctx, (const unsigned char*)p, (unsigned)n); }
  // Little-endian, so the C# side can hash the same bytes on any host.
  void AddI32 (int32_t v) {
    uint32_t u = (uint32_t)v;
    unsigned char b[4] = {(unsigned char)u, (unsigned char) (u >> 8), (unsigned char) (u >> 16), (unsigned char) (u >> 24)};
    Add (b, 4);
  }
  void AddI64 (int64_t v) {
    AddI32 ((int32_t) (uint32_t)v);
    AddI32 ((int32_t) (uint32_t) ((uint64_t)v >> 32));
  }
  std::string Hex() {
    unsigned char d[SHA_DIGEST_LENGTH];
    SHA1Result (&ctx, d);
    char hex[SHA_DIGEST_LENGTH * 2 + 1];
    for (int i = 0; i < SHA_DIGEST_LENGTH; ++i) snprintf (&hex[i * 2], 3, "%02x", d[i]);
    return hex;
  }
};

inline void Emit (const char* group, const char* kernel, const std::string& kase, Hasher& h) {
  printf ("%s\t%s\t%s\t%s\n", group, kernel, kase.c_str(), h.Hex().c_str());
}

}  // namespace fixture

// One function per ported common/*.cpp group; each prints its rows.
void KernelsTables();
void KernelsMacros();
void KernelsCopyMb();
void KernelsExpandPic();
void KernelsIntraPred();
void KernelsMc();
void KernelsDeblock();
void KernelsSad();
void KernelsDecoderTables();
void KernelsDecodeMbAux();
void KernelsIntraPredDec();

#endif  // H264REF_FIXTURE_H
