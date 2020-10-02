// これは メイン DLL ファイルです。

#include "stdafx.h"
#include <vcclr.h> // PtrToStringChars
#include "Decoder.h"

using namespace System::Drawing;
using namespace System::Drawing::Imaging;

namespace OpenH264Lib {

	// Reference
	// file://openh264-master\test\api\BaseEncoderTest.cpp
	// file://openh264-master\codec\console\enc\src\welsenc.cpp

	///<summary>Decode h264 frame data to Bitmap.</summary>
	///<returns>Bitmap. Might be null if frame data is incomplete.</returns>
	Bitmap^ Decoder::Decode(array<Byte> ^frame, int length)
	{
		// http://xptn.dtiblog.com/blog-entry-21.html
		pin_ptr<Byte> ptr = &frame[0];
		Bitmap^ rc = Decode(ptr, length);
		ptr = nullptr; // unpin
		return rc;
	}

	Bitmap^ Decoder::Decode(unsigned char *frame, int length)
	{
		unsigned char* buffer[3];

		SBufferInfo bufInfo; memset(&bufInfo, 0x00, sizeof(bufInfo));
		int rc = decoder->DecodeFrame2(frame, length, buffer, &bufInfo);
		if (rc != 0) return nullptr;
		if (bufInfo.iBufferStatus != 1) return nullptr;

		// Y Plane(luma)
		byte* y_plane = buffer[0];
		int y_w = bufInfo.UsrData.sSystemBuffer.iWidth;
		int y_h = bufInfo.UsrData.sSystemBuffer.iHeight;
		int y_s = bufInfo.UsrData.sSystemBuffer.iStride[0];

		// U Plane(chroma B - Y')
		byte* u_plane = buffer[1];
		int u_w = bufInfo.UsrData.sSystemBuffer.iWidth / 2;
		int u_h = bufInfo.UsrData.sSystemBuffer.iHeight / 2;
		int u_s = bufInfo.UsrData.sSystemBuffer.iStride[1];

		// V Plane(chroma R - Y')
		byte* v_plane = buffer[2];
		int v_w = bufInfo.UsrData.sSystemBuffer.iWidth / 2;
		int v_h = bufInfo.UsrData.sSystemBuffer.iHeight / 2;
		int v_s = bufInfo.UsrData.sSystemBuffer.iStride[1];

		int width = y_w;
		int height = y_h;
		int stride = y_s;

		byte* rgb = YUV420PtoRGB(y_plane, v_plane, u_plane, width, height, stride); // なぜかuvが逆。
		Bitmap^ result = RGBtoBitmap(rgb, width, height);
		delete rgb;

		return result;
	}

	int Decoder::Setup()
	{
		SDecodingParam decParam;
		memset(&decParam, 0, sizeof(SDecodingParam));
		decParam.uiTargetDqLayer = UCHAR_MAX;
		decParam.eEcActiveIdc = ERROR_CON_SLICE_COPY;
		decParam.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_DEFAULT;
		int rc = decoder->Initialize(&decParam);
		if (rc != 0) return -1;

		return 0;
	};

	// コンストラクタ
	// dllName:"openh264-1.7.0-win32.dll"のような文字列を指定する。
	Decoder::Decoder(String ^dllName)
	{
		// Load DLL
		pin_ptr<const wchar_t> dllname = PtrToStringChars(dllName);
		HMODULE hDll = LoadLibrary(dllname);
		if (hDll == NULL) throw gcnew System::DllNotFoundException(String::Format("Unable to load '{0}'", dllName));
		dllname = nullptr;

		CreateDecoderFunc = (WelsCreateDecoderFunc)GetProcAddress(hDll, "WelsCreateDecoder");
		if (CreateDecoderFunc == NULL) throw gcnew System::DllNotFoundException(String::Format("Unable to load WelsCreateDecoder func in '{0}'", dllName));
		DestroyDecoderFunc = (WelsDestroyDecoderFunc)GetProcAddress(hDll, "WelsDestroyDecoder");
		if (DestroyDecoderFunc == NULL) throw gcnew System::DllNotFoundException(String::Format("Unable to load WelsDestroyDecoder func in '{0}'"));

		ISVCDecoder* dec = nullptr;
		int rc = CreateDecoderFunc(&dec);
		decoder = dec;
		if (rc != 0) throw gcnew System::DllNotFoundException(String::Format("Unable to call WelsCreateSVCDecoder func in '{0}'"));

		rc = Setup();
		if (rc != 0) throw gcnew System::InvalidOperationException("Error occurred during initializing decoder.");
	}

	// デストラクタ：リソースを積極的に解放する為にあるメソッド。C#のDisposeに対応。
	// マネージド、アンマネージド両方とも解放する。
	Decoder::~Decoder()
	{
		// マネージド解放→なし
		// ファイナライザ呼び出し
		this->!Decoder();
	}

	// ファイナライザ：リソースの解放し忘れによる被害を最小限に抑える為にあるメソッド。
	// アンマネージドリソースを解放する。
	Decoder::!Decoder()
	{
		// アンマネージド解放
		decoder->Uninitialize();
		DestroyDecoderFunc(decoder);
	}

	byte* Decoder::YUV420PtoRGB(byte* yplane, byte* uplane, byte* vplane, int width, int height, int stride)
	{
		// https://www.ite.or.jp/contents/keywords/FILE-20120103130828.pdf
		// Yは輝度、Uは赤、Vは青との色差。
		// 人間の視覚は例えば暗闇では形状は認識できても色は認識しにくいように、輝度に比べ色に対する感度が低い
		// RGBをYUVに変換すると、それを利用して画像を効率的に圧縮することができる。
		// Yはそのままにし、UVを水平方向に半分にしたものを4:2:2、水平&垂直に半分にしたものを4:2:0フォーマットという。

		// https://msdn.microsoft.com/ja-jp/library/windows/desktop/dd206750(v=vs.85).aspx
		// [4:2:0 Formats, 16 Bits per Pixel]
		// +-----------------+
		// |                 |  YUV formats are planar formats. 
		// |     Y Plane     |  The chroma channels are subsampled by a factor of two in both the horizontal and vertical dimensions.
		// |                 |  Y samples appear first in memory.
		// +--------+--------+  The V and U planes have the same stride as the Y plane.
		// |  V Pln |        |  The U and V planes must start on memory boundaries that are a multiple of 16 lines.
		// +--------+ unused |
		// +--------+        |
		// |  U Pln |        |
		// +--------+--------+

		// Reference
		// https://stackoverflow.com/questions/16107165/convert-from-yuv-420-to-imagebgr-byte/16108293
		// https://gist.github.com/RicardoRodriguezPina/b90c4cef9c1646c0a9fe7faea8e06d63

		byte* result = new byte[width * height * 3];
		byte* rgb = result;

		for (int y = 0; y < height; y++)
		{
			// 行の先頭へのポインタ
			int rowIdx = (stride * y);
			int uvpIdx = (stride / 2) * (y / 2);

			byte* pYp = yplane + rowIdx;
			byte* pUp = uplane + uvpIdx;
			byte* pVp = vplane + uvpIdx;

			// 2Pixelずつ処理する。
			for (int x = 0; x < width; x += 2)
			{
				int C1 = pYp[0] - 16; // Yの下限は16
				int C2 = pYp[1] - 16; // Yの下限は16
				int D = *pUp - 128;  // 128で無色差
				int E = *pVp - 128;  // 128で無色差

				int R1 = (298 * C1 + 409 * E + 128) >> 8;
				int G1 = (298 * C1 - 100 * D - 208 * E + 128) >> 8;
				int B1 = (298 * C1 + 516 * D + 128) >> 8;

				int R2 = (298 * C2 + 409 * E + 128) >> 8;
				int G2 = (298 * C2 - 100 * D - 208 * E + 128) >> 8;
				int B2 = (298 * C2 + 516 * D + 128) >> 8;

				rgb[0] = (byte)(R1 < 0 ? 0 : R1 > 255 ? 255 : R1);
				rgb[1] = (byte)(G1 < 0 ? 0 : G1 > 255 ? 255 : G1);
				rgb[2] = (byte)(B1 < 0 ? 0 : B1 > 255 ? 255 : B1);

				rgb[3] = (byte)(R2 < 0 ? 0 : R2 > 255 ? 255 : R2);
				rgb[4] = (byte)(G2 < 0 ? 0 : G2 > 255 ? 255 : G2);
				rgb[5] = (byte)(B2 < 0 ? 0 : B2 > 255 ? 255 : B2);

				rgb += 6;
				pYp += 2;
				pUp += 1;
				pVp += 1;
			}
		}

		return result;
	}

	Bitmap^ Decoder::RGBtoBitmap(byte* rgb, int width, int height)
	{
		int pixelSize = 3;
		Bitmap^ bmp = gcnew Bitmap(width, height, System::Drawing::Imaging::PixelFormat::Format24bppRgb);
		BitmapData^ bmpDate = bmp->LockBits(System::Drawing::Rectangle(0, 0, width, height), ImageLockMode::WriteOnly, bmp->PixelFormat);
		byte *ptr = (byte *)bmpDate->Scan0.ToPointer();

		int cnt = 0;
		for (int y = 0; y <= height - 1; y++)
		{
			for (int x = 0; x <= width - 1; x++)
			{
				//ピクセルデータでのピクセル(x,y)の開始位置を計算する
				int pos = y * bmpDate->Stride + x * pixelSize;

				ptr[pos + 0] = rgb[cnt + 0]; // r
				ptr[pos + 1] = rgb[cnt + 1]; // g
				ptr[pos + 2] = rgb[cnt + 2]; // b
				cnt += 3;
			}
		}

		//ロックを解除する
		bmp->UnlockBits(bmpDate);

		return bmp;
	}
}
