#include "Windows.h"

#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavformat/avio.h>
#include <libavutil/imgutils.h>
#include <libswscale/swscale.h>
#include <libavutil/time.h>

#define WIDTH 32
#define HEIGHT 32
#define ERROR_LEN 128
#define SWS_FLAGS SWS_BICUBIC

char _errorLog[ERROR_LEN];
void CreateBitmapFile(LPCWSTR fileName, long width, long height, WORD bitsPerPixel, BYTE* bitmapData, DWORD bitmapDataLength);

int mainz()
{
  printf("FFmpeg Pixel Conversion Test\n");

  av_log_set_level(AV_LOG_DEBUG);

  int w = WIDTH;
  int h = HEIGHT;

  struct SwsContext* rgbToI420Context;
  struct SwsContext* i420ToRgbContext;

  rgbToI420Context = sws_getContext(w, h, AV_PIX_FMT_RGB24, w, h, AV_PIX_FMT_YUV420P, SWS_FLAGS, NULL, NULL, NULL);
  if (rgbToI420Context == NULL) {
    fprintf(stderr, "Failed to allocate RGB to I420 conversion context.\n");
  }

  i420ToRgbContext = sws_getContext(w, h, AV_PIX_FMT_YUV420P, w, h, AV_PIX_FMT_RGB24, SWS_FLAGS, NULL, NULL, NULL);
  if (i420ToRgbContext == NULL) {
    fprintf(stderr, "Failed to allocate I420 to RGB conversion context.\n");
  }

  // Create dummy bitmap.
  uint8_t rgbRaw[WIDTH * HEIGHT * 3];
  for (int row = 0; row < 32; row++)
  {
    for (int col = 0; col < 32; col++)
    {
      int index = row * WIDTH * 3 + col * 3;

      int red = (row < 16 && col < 16) ? 255 : 0;
      int green = (row < 16 && col > 16) ? 255 : 0;
      int blue = (row > 16 && col < 16) ? 255 : 0;

      rgbRaw[index] = (byte)red;
      rgbRaw[index + 1] = (byte)green;
      rgbRaw[index + 2] = (byte)blue;
    }
  }

  CreateBitmapFile(L"test-reference.bmp", WIDTH, HEIGHT, 24, rgbRaw, WIDTH * HEIGHT * 3);

  printf("Converting RGB to I420.\n");

  uint8_t* rgb[3];
  uint8_t* i420[3];
  int rgbStride[3], i420Stride[3];

  rgbStride[0] = w * 3;
  i420Stride[0] = w * h;
  i420Stride[1] = w * h / 4;
  i420Stride[2] = w * h / 4;

  rgb[0] = rgbRaw;
  i420[0] = (uint8_t*)malloc((size_t)i420Stride[0] * h);
  i420[1] = (uint8_t*)malloc((size_t)i420Stride[1] * h);
  i420[2] = (uint8_t*)malloc((size_t)i420Stride[2] * h);

  int toI420Res = sws_scale(rgbToI420Context, rgb, rgbStride, 0, h, i420, i420Stride);
  if (toI420Res < 0) {
    fprintf(stderr, "Conversion from RGB to I420 failed, %s.\n", av_make_error_string(_errorLog, ERROR_LEN, toI420Res));
  }

  printf("Converting I420 to RGB.\n");

  uint8_t* rgbOut[3];
  int rgbOutStride[3];

  rgbOutStride[0] = w * 3;
  rgbOut[0] = (uint8_t*)malloc((size_t)rgbOutStride[0] * h);

  int toRgbRes = sws_scale(i420ToRgbContext, i420, i420Stride, 0, h, rgbOut, rgbOutStride);
  if (toRgbRes < 0) {
    fprintf(stderr, "Conversion from RGB to I420 failed, %s.\n", av_make_error_string(_errorLog, ERROR_LEN, toRgbRes));
  }

  CreateBitmapFile(L"test-output.bmp", WIDTH, HEIGHT, 24, rgbOut, WIDTH * HEIGHT * 3);

  free(rgbOut[0]);

  for (int i = 0; i < 3; i++) {
    free(i420[i]);
  }

  sws_freeContext(rgbToI420Context);
  sws_freeContext(i420ToRgbContext);

  return 0;
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