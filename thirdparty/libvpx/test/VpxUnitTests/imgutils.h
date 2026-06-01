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

#ifndef IMGUTILS_H
#define IMGUTILS_H

#include "Windows.h"
#include "vpx/vpx_image.h"

#include <iomanip>
#include <sstream>
#include <string>
#include <vector>

namespace
{
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

  std::vector<uint8_t> I420toBGR(
    uint8_t* yPlane, int yStride,
    uint8_t* uPlane, int uStride,
    uint8_t* vPlane, int vStride,
    int width, int height)
  {
    int size = width * height;
    std::vector<uint8_t> rgb(size * 3);
    int posn = 0;
    int u, v, y;
    int r, g, b;

    for (int row = 0; row < height; row++)
    {
      for (int col = 0; col < width; col++)
      {
        y = yPlane[col + row * yStride];
        u = uPlane[col / 2 + (row / 2) * uStride] - 128;
        v = vPlane[col / 2 + (row / 2) * vStride] - 128;

        r = (int)(y + 1.140 * v);
        g = (int)(y - 0.395 * u - 0.581 * v);
        b = (int)(y + 2.302 * u);

        rgb[posn++] = (uint8_t)(b > 255 ? 255 : b < 0 ? 0 : b);
        rgb[posn++] = (uint8_t)(g > 255 ? 255 : g < 0 ? 0 : g);
        rgb[posn++] = (uint8_t)(r > 255 ? 255 : r < 0 ? 0 : r);
      }
    }

    return rgb;
  }
}

#endif IMGUTILS_H