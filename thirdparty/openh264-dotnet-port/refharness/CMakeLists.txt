# Reference build of OpenH264 for the SIPSorcery.H264 port.
#
# Compiles the pinned openh264 checkout with NO assembly and NO SIMD defines
# (no X86_ASM / HAVE_AVX2 / HAVE_NEON ...), so only the portable *_c kernels
# are compiled in. That is exactly the code being ported to C#, which makes
# this build the bit-exact oracle for the port. See README.md.
#
#   cmake -S . -B build -G "Visual Studio 17 2022" -A x64
#   cmake --build build --config Release

cmake_minimum_required(VERSION 3.20)
project(openh264_refharness C CXX)

set(OPENH264_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../../openh264" CACHE PATH
    "Path to the openh264 checkout at the pinned commit (see README.md).")
get_filename_component(OPENH264_DIR "${OPENH264_DIR}" ABSOLUTE)
if(NOT EXISTS "${OPENH264_DIR}/codec/api/wels/codec_api.h")
  message(FATAL_ERROR "OPENH264_DIR=${OPENH264_DIR} is not an openh264 checkout.")
endif()
set(OH "${OPENH264_DIR}/codec")

if(MSVC)
  # Same floating-point model as upstream's msvc-common.mk.
  add_compile_options(/nologo /W3 /EHsc /fp:precise /D_CRT_SECURE_NO_WARNINGS /wd4244 /wd4267 /wd4018)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

# codec/common (list from codec/common/meson.build, minus the arch folders).
set(COMMON_SRC
  common_tables copy_mb cpu crt_util_safe_x deblocking_common expand_pic
  intra_pred_common mc memory_align sad_common utils welsCodecTrace
  WelsTaskThread WelsThread WelsThreadLib WelsThreadPool)
list(TRANSFORM COMMON_SRC PREPEND "${OH}/common/src/")
list(TRANSFORM COMMON_SRC APPEND ".cpp")
add_library(oh_common STATIC ${COMMON_SRC})
target_include_directories(oh_common PUBLIC "${OH}/api/wels" "${OH}/common/inc")

# codec/decoder (list from codec/decoder/meson.build, minus the arch folders).
set(DECODER_SRC
  au_parser bit_stream cabac_decoder deblocking decode_mb_aux decode_slice
  decoder decoder_core decoder_data_tables error_concealment fmo
  get_intra_predictor manage_dec_ref memmgr_nal_unit mv_pred
  parse_mb_syn_cabac parse_mb_syn_cavlc pic_queue rec_mb wels_decoder_thread)
list(TRANSFORM DECODER_SRC PREPEND "${OH}/decoder/core/src/")
list(TRANSFORM DECODER_SRC APPEND ".cpp")
add_library(oh_decoder STATIC ${DECODER_SRC} "${OH}/decoder/plus/src/welsDecoderExt.cpp")
target_include_directories(oh_decoder PUBLIC "${OH}/decoder/core/inc" "${OH}/decoder/plus/inc")
target_link_libraries(oh_decoder PUBLIC oh_common)

# The harness: conformance hashes, stream feature classification, YUV dumps,
# the parser header dumps (src/headers.cpp) and the kernel fixtures (src/fixture.h, src/kernels_*.cpp).
add_executable(h264ref
  src/main.cpp
  src/headers.cpp
  src/stream_info.cpp
  src/kernels_common.cpp
  src/kernels_mc.cpp
  src/kernels_deblock_sad.cpp
  src/kernels_decoder.cpp
  "${OPENH264_DIR}/test/api/sha1.c")
target_include_directories(h264ref PRIVATE "${OPENH264_DIR}/test" src)
target_compile_features(h264ref PRIVATE cxx_std_17)
target_link_libraries(h264ref PRIVATE oh_decoder)
