#include "Windows.h""
#include "strutils.h"
#include "vp8cx.h"
#include "vp8dx.h"
#include "vpx_decoder.h"
#include "vpx_encoder.h"

#include <iostream>
#include <vector>

std::vector<uint8_t> convertYV12toRGB(const vpx_image_t* img);
void CreateBitmapFile(LPCWSTR fileName, long width, long height, WORD bitsPerPixel, BYTE* bitmapData, DWORD bitmapDataLength);

inline int clamp8(int v)
{
  return min(max(v, 0), 255);
}

int main()
{
  std::cout << "libvpx test console\n";

  std::cout << "vp8 encoder version " << vpx_codec_version_str() << "." << std::endl;
  std::cout << "VPX_ENCODER_ABI_VERSION=" << VPX_ENCODER_ABI_VERSION << "." << std::endl;
  std::cout << "VPX_DECODER_ABI_VERSION=" << VPX_DECODER_ABI_VERSION << "." << std::endl;

  int width = 640;
  int height = 480;
  int stride = 1;

  vpx_codec_ctx_t codec;
  vpx_image_t* img{ nullptr };
  vpx_codec_ctx_t decoder;

  img = vpx_img_alloc(NULL, VPX_IMG_FMT_I420, width, height, stride);

  vpx_codec_enc_cfg_t vpxConfig;
  vpx_codec_err_t res;

  // Initialise codec configuration.
  res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);

  if (res) {
    printf("Failed to get VPX codec config: %s\n", vpx_codec_err_to_string(res));
    return -1;
  }

  vpxConfig.g_w = width;
  vpxConfig.g_h = height;

  // Initialise codec.
  res = vpx_codec_enc_init(&codec, vpx_codec_vp8_cx(), &vpxConfig, 0);

  if (res) {
    printf("Failed to initialise VPX codec: %s\n", vpx_codec_err_to_string(res));
    return -1;
  }

  // Do a test encode.
  std::vector<uint8_t> dummyI420(640 * 480 * 3 / 2);
  vpx_enc_frame_flags_t flags = 0;

  vpx_img_wrap(img, VPX_IMG_FMT_I420, width, height, 1, dummyI420.data());

  res = vpx_codec_encode(&codec, img, 1, 1, flags, VPX_DL_REALTIME);
  if (res) {
    printf("VPX codec failed to encode dummy frame. %s\n", vpx_codec_err_to_string(res));
    return -1;
  }

  vpx_codec_iter_t iter = NULL;
  const vpx_codec_cx_pkt_t* pkt;

  //while ((pkt = vpx_codec_get_cx_data(&codec, &iter))) {
  pkt = vpx_codec_get_cx_data(&codec, &iter);
  switch (pkt->kind) {
  case VPX_CODEC_CX_FRAME_PKT:
    printf("Encode success %s %i\n", (pkt->data.frame.flags & VPX_FRAME_IS_KEY) ? "K" : ".", pkt->data.frame.sz);
    break;
  default:
    printf("Got unknown packet type %d.\n", pkt->kind);
    break;
  }
  //}

  // Attempt to decode.
  res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);
  if (res) {
    printf("Failed to initialise VPX decoder: %s\n", vpx_codec_err_to_string(res));
    return -1;
  }

  res = vpx_codec_decode(&decoder, (const uint8_t*)pkt->data.frame.buf, pkt->data.frame.sz, nullptr, 0);
  if (res) {
    printf("Failed to decode buffer: %s\n", vpx_codec_err_to_string(res));
    return -1;
  }

  vpx_codec_iter_t decoder_iter = NULL;
  vpx_image_t* decodedImg = vpx_codec_get_frame(&decoder, &decoder_iter);
  
  if (decodedImg != NULL) {
    printf("Decode successful, width %d, height %d.\n", decodedImg->d_w, decodedImg->d_h);

    for (int i = 0; i < 4; i++) {
      printf("stride[%d]=%d, plane[%d]=%d.\n", i, decodedImg->stride[i], i, decodedImg->planes[i]);
    }

    auto rgb = convertYV12toRGB(decodedImg);

    CreateBitmapFile(L"test-decode.bmp", width, height, 24, rgb.data(), width * height * 3);
  }
}

std::vector<uint8_t> convertYV12toRGB(const vpx_image_t* img)
{
  std::vector<uint8_t> data (img->d_w * img->d_h * 3);

  uint8_t* yPlane = img->planes[VPX_PLANE_Y];
  uint8_t* uPlane = img->planes[VPX_PLANE_U];
  uint8_t* vPlane = img->planes[VPX_PLANE_V];

  int i = 0;
  for (unsigned int imgY = 0; imgY < img->d_h; imgY++) {
    for (unsigned int imgX = 0; imgX < img->d_w; imgX++) {
      int y = yPlane[imgY * img->stride[VPX_PLANE_Y] + imgX];
      int u = uPlane[(imgY / 2) * img->stride[VPX_PLANE_U] + (imgX / 2)];
      int v = vPlane[(imgY / 2) * img->stride[VPX_PLANE_V] + (imgX / 2)];

      int c = y - 16;
      int d = (u - 128);
      int e = (v - 128);

      // TODO: adjust colors ?

      int r = clamp8((298 * c + 409 * e + 128) >> 8);
      int g = clamp8((298 * c - 100 * d - 208 * e + 128) >> 8);
      int b = clamp8((298 * c + 516 * d + 128) >> 8);

      // TODO: cast instead of clamp8

      data[i + 0] = static_cast<uint8_t>(r);
      data[i + 1] = static_cast<uint8_t>(g);
      data[i + 2] = static_cast<uint8_t>(b);

      i += 3;
    }
  }
  return data;
}

/**
* Creates a bitmap file and writes to disk.
* @param[in] fileName: the path to save the file at.
* @param[in] width: the width of the bitmap.
* @param[in] height: the height of the bitmap.
* @param[in] bitsPerPixel: colour depth of the bitmap pixels (typically 24 or 32).
* @param[in] bitmapData: a pointer to the bytes containing the bitmap data.
* @param[in] bitmapDataLength: the number of pixels in the bitmap.
*/
void CreateBitmapFile(LPCWSTR fileName, long width, long height, WORD bitsPerPixel, BYTE* bitmapData, DWORD bitmapDataLength)
{
  HANDLE file;
  BITMAPFILEHEADER fileHeader;
  BITMAPINFOHEADER fileInfo;
  DWORD writePosn = 0;

  file = CreateFile(fileName, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);  //Sets up the new bmp to be written to

  fileHeader.bfType = 19778;                                                                    //Sets our type to BM or bmp
  fileHeader.bfSize = sizeof(fileHeader.bfOffBits) + sizeof(RGBTRIPLE);                         //Sets the size equal to the size of the header struct
  fileHeader.bfReserved1 = 0;                                                                   //sets the reserves to 0
  fileHeader.bfReserved2 = 0;
  fileHeader.bfOffBits = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER);											//Sets offbits equal to the size of file and info header
  fileInfo.biSize = sizeof(BITMAPINFOHEADER);
  fileInfo.biWidth = width;
  fileInfo.biHeight = height;
  fileInfo.biPlanes = 1;
  fileInfo.biBitCount = bitsPerPixel;
  fileInfo.biCompression = BI_RGB;
  fileInfo.biSizeImage = width * height * (bitsPerPixel / 8);
  fileInfo.biXPelsPerMeter = 2400;
  fileInfo.biYPelsPerMeter = 2400;
  fileInfo.biClrImportant = 0;
  fileInfo.biClrUsed = 0;

  WriteFile(file, &fileHeader, sizeof(fileHeader), &writePosn, NULL);

  WriteFile(file, &fileInfo, sizeof(fileInfo), &writePosn, NULL);

  WriteFile(file, bitmapData, bitmapDataLength, &writePosn, NULL);

  CloseHandle(file);
}

