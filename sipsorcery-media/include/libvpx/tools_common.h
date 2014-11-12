/*
 *  Copyright (c) 2010 The WebM project authors. All Rights Reserved.
 *
 *  Use of this source code is governed by a BSD-style license
 *  that can be found in the LICENSE file in the root of the source
 *  tree. An additional intellectual property rights grant can be found
 *  in the file PATENTS.  All contributing project authors may
 *  be found in the AUTHORS file in the root of the source tree.
 */
#ifndef TOOLS_COMMON_H_
#define TOOLS_COMMON_H_

#include <stdio.h>

#include "./vpx_config.h"

#if defined(_MSC_VER)
/* MSVS doesn't define off_t, and uses _f{seek,tell}i64. */
typedef __int64 off_t;
#define fseeko _fseeki64
#define ftello _ftelli64
#elif defined(_WIN32)
/* MinGW defines off_t as long and uses f{seek,tell}o64/off64_t for large
 * files. */
#define fseeko fseeko64
#define ftello ftello64
#define off_t off64_t
#endif  /* _WIN32 */

#if CONFIG_OS_SUPPORT
#if defined(_MSC_VER)
#include <io.h>  /* NOLINT */
#define snprintf _snprintf
#define isatty   _isatty
#define fileno   _fileno
#else
#include <unistd.h>  /* NOLINT */
#endif  /* _MSC_VER */
#endif  /* CONFIG_OS_SUPPORT */

/* Use 32-bit file operations in WebM file format when building ARM
 * executables (.axf) with RVCT. */
#if !CONFIG_OS_SUPPORT
typedef long off_t;  /* NOLINT */
#define fseeko fseek
#define ftello ftell
#endif  /* CONFIG_OS_SUPPORT */

#define LITERALU64(hi, lo) ((((uint64_t)hi) << 32) | lo)

#ifndef PATH_MAX
#define PATH_MAX 512
#endif

#define VP8_FOURCC (0x30385056)
#define VP9_FOURCC (0x30395056)
#define VP8_FOURCC_MASK (0x00385056)
#define VP9_FOURCC_MASK (0x00395056)

/* Sets a stdio stream into binary mode */
FILE *set_binary_mode(FILE *stream);

void die(const char *fmt, ...);
void fatal(const char *fmt, ...);
void warn(const char *fmt, ...);

/* The tool including this file must define usage_exit() */
void usage_exit();

#endif  // TOOLS_COMMON_H_
