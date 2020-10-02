// これは メイン DLL ファイルです。

#include "stdafx.h"
#include <vcclr.h> // PtrToStringChars
#include "Encoder.h"

using namespace System::Drawing;
using namespace System::Drawing::Imaging;

namespace OpenH264Lib {

	// Reference
	// https://github.com/kazuki/video-codec.js
	// file://openh264-master\test\api\BaseEncoderTest.cpp
	// file://openh264-master\codec\console\enc\src\welsenc.cpp

	// Obsoleted and will deleted future release.
	int Encoder::Encode(Bitmap^ bmp, float timestamp)
	{
		return Encode(bmp);
	}

	// Encode Bitmap to H264 frame data.
	int Encoder::Encode(Bitmap^ bmp)
	{
		if (pic->iPicWidth != bmp->Width || pic->iPicHeight != bmp->Height) throw gcnew System::ArgumentException("Image width and height must be same.");

		unsigned char* rgba = BitmapToRGBA(bmp, bmp->Width, bmp->Height);
		unsigned char* i420 = RGBAtoYUV420Planar(rgba, bmp->Width, bmp->Height);
		int rc = Encode(i420);
		delete rgba;
		delete i420;

		return rc;
	}

	// C#からbyte[]として呼び出し可
	int Encoder::Encode(array<Byte> ^i420)
	{
		// http://xptn.dtiblog.com/blog-entry-21.html
		pin_ptr<Byte> ptr = &i420[0];
		int rc = Encode(ptr);
		ptr = nullptr; // unpin
		return rc;
	}

	// C#からはunsafe&fixedを利用しないと呼び出しできない
	int Encoder::Encode(unsigned char *i420)
	{
		memcpy(i420_buffer, i420, buffer_size);

		// insert key frame at periodic interval.
		// 一定間隔でキーフレーム(Iフレーム)を挿入
		if (num_of_frames++ % keyframe_interval == 0) {
			encoder->ForceIntraFrame(true);
		}

		// comment out.
		//bsi->uiTimeStamp seems to incresed automatically.
		//pic->uiTimeStamp = (long long)(timestamp * 1000.0f);
		
		// encode frame.
		// フレームエンコード
		int ret = encoder->EncodeFrame(pic, bsi);
		if (ret != 0) {
			return ret;
		}

		// encode completed callback.
		// エンコード完了コールバック
		if (bsi->eFrameType != videoFrameTypeSkip) {
			OnEncode(dynamic_cast<SFrameBSInfo%>(*bsi));
		}

		return 0;
	}

	void Encoder::OnEncode(const SFrameBSInfo% info)
	{
		for (int i = 0; i < info.iLayerNum; ++i) {
			const SLayerBSInfo& layerInfo = info.sLayerInfo[i];
			int layerSize = 0;
			for (int j = 0; j < layerInfo.iNalCount; ++j) {
				layerSize += layerInfo.pNalLengthInByte[j];
			}

			//bool keyFrame = (info.eFrameType == videoFrameTypeIDR) || (info.eFrameType == videoFrameTypeI);
			//OnEncodeFunc(layerInfo.pBsBuf, layerSize, keyFrame);

			array<Byte>^ data = gcnew array<Byte>(layerSize);
			//for(int j = 0; j < layerSize; j++) ary[j] = layerInfo.pBsBuf[j];
			/*pin_ptr<Byte> dataPtr = &data[0];
			memcpy(dataPtr, layerInfo.pBsBuf, layerSize);
			dataPtr = nullptr;*/
			System::Runtime::InteropServices::Marshal::Copy((IntPtr)layerInfo.pBsBuf, data, 0, layerSize);
			OnEncodeFunc(data, layerSize, (FrameType)info.eFrameType);
		}
	}

	// エンコーダーの設定(Encoder Setup.)
	// width, height:画像サイズ(image size.)
	// bps:ターゲットビットレート。openH264のデフォルト値「5000000」で「5Mbps」。(target bitrate. e.g. 5000000.)
	// fps:フレームレート(frame rate.)
	// keyFrameInterval:キーフレーム(Iフレーム)挿入間隔を秒で指定。通常の動画(30fps)では2秒ごと位が適切らしい。(insert key frame interval. unit is second. e.g. 2(sec).)
	// onEncode:1フレームエンコードするごとに呼び出されるコールバック。(callback function when each frame encoded.)
	int Encoder::Setup(int width, int height, int bps, float fps, float keyFrameInterval, OnEncodeCallback^ onEncode)
	{
		//OnEncodeFunc = static_cast<OnEncodeFuncPtr>(onEncode->ToPointer());
		//OnEncodeFunc = static_cast<OnEncodeFuncPtr>(onEncode);
		OnEncodeFunc = onEncode;

		// 何フレームごとにキーフレーム(Iフレーム)を挿入するか
		// 通常の動画(30fps)では60(つまり2秒ごと)位が適切らしい。
		keyframe_interval = (int)(fps * keyFrameInterval);

		// encoderの初期化。encoder->Initializeを使う。
		// encoder->InitializeExtは初期化に必要なパラメータが多すぎる
		SEncParamBase base;
		memset(&base, 0, sizeof(SEncParamBase));
		base.iPicWidth = width;
		base.iPicHeight = height;
		base.fMaxFrameRate = fps;
		base.iUsageType = CAMERA_VIDEO_REAL_TIME;
		base.iTargetBitrate = bps;
		base.iRCMode = RC_BITRATE_MODE;

		int rc = encoder->Initialize(&base);
		if (rc != 0) return -1;

		// ソースフレームメモリ確保
		buffer_size = width * height * 3 / 2;
		i420_buffer = new unsigned char[buffer_size];
		pic = new SSourcePicture();
		pic->iPicWidth = width;
		pic->iPicHeight = height;
		pic->iColorFormat = videoFormatI420;
		pic->iStride[0] = pic->iPicWidth;
		pic->iStride[1] = pic->iStride[2] = pic->iPicWidth >> 1;
		pic->pData[0] = i420_buffer;
		pic->pData[1] = pic->pData[0] + width * height;
		pic->pData[2] = pic->pData[1] + (width * height >> 2);

		// ビットストリーム確保
		bsi = new SFrameBSInfo();

		return 0;
	};

	// コンストラクタ
	// dllName:"openh264-1.7.0-win32.dll"のような文字列を指定する。
	Encoder::Encoder(String ^dllName)
	{
		// Load DLL
		pin_ptr<const wchar_t> dllname = PtrToStringChars(dllName);
		HMODULE hDll = LoadLibrary(dllname);
		if (hDll == NULL) throw gcnew System::DllNotFoundException(String::Format("Unable to load '{0}'", dllName));
		dllname = nullptr;

		// Load Function
		CreateEncoderFunc = (WelsCreateSVCEncoderFunc)GetProcAddress(hDll, "WelsCreateSVCEncoder");
		if (CreateEncoderFunc == NULL) throw gcnew System::DllNotFoundException(String::Format("Unable to load WelsCreateSVCEncoder func in '{0}'", dllName));
		DestroyEncoderFunc = (WelsDestroySVCEncoderFunc)GetProcAddress(hDll, "WelsDestroySVCEncoder");
		if (DestroyEncoderFunc == NULL) throw gcnew System::DllNotFoundException(String::Format("Unable to load WelsDestroySVCEncoder func in '{0}'"));

		// encoderはマネージヒープ上に存在するため、ポインタに変換することができない
		//CreateEncoderFunc(&encoder);
		ISVCEncoder* enc = nullptr;
		int rc = CreateEncoderFunc(&enc);
		encoder = enc;
		if (rc != 0) throw gcnew System::DllNotFoundException(String::Format("Unable to call WelsCreateSVCEncoder func in '{0}'"));
	}

	// デストラクタ：リソースを積極的に解放する為にあるメソッド。C#のDisposeに対応。
	// マネージド、アンマネージド両方とも解放する。
	Encoder::~Encoder()
	{
		// マネージド解放→なし
		// ファイナライザ呼び出し
		this->!Encoder();
	}

	// ファイナライザ：リソースの解放し忘れによる被害を最小限に抑える為にあるメソッド。
	// アンマネージドリソースを解放する。
	Encoder::!Encoder()
	{
		// アンマネージド解放
		encoder->Uninitialize();
		DestroyEncoderFunc(encoder);

		delete i420_buffer;
		delete pic;
		delete bsi;
	}

	unsigned char* Encoder::BitmapToRGBA(Bitmap^ bmp, int width, int height)
	{
		//1ピクセルあたりのバイト数を取得する
		int pixelSize = 0;
		switch (bmp->PixelFormat)
		{
		case System::Drawing::Imaging::PixelFormat::Format24bppRgb:
			pixelSize = 3;
			break;
		case System::Drawing::Imaging::PixelFormat::Format32bppArgb:
		case System::Drawing::Imaging::PixelFormat::Format32bppPArgb:
		case System::Drawing::Imaging::PixelFormat::Format32bppRgb:
			pixelSize = 4;
			break;
		default:
			throw gcnew ArgumentException("1ピクセルあたり24または32ビットの形式のイメージのみ有効です。");
		}

		BitmapData^ bmpDate = bmp->LockBits(System::Drawing::Rectangle(0, 0, width, height), ImageLockMode::ReadOnly, bmp->PixelFormat);
		byte *ptr = (byte *)bmpDate->Scan0.ToPointer();

		unsigned char* buffer = new unsigned char[width * height * 4];

		int cnt = 0;
		for (int y = 0; y <= height - 1; y++)
		{
			for (int x = 0; x <= width - 1; x++)
			{
				//ピクセルデータでのピクセル(x,y)の開始位置を計算する
				int pos = y * bmpDate->Stride + x * pixelSize;

				buffer[cnt + 0] = ptr[pos + 0]; // r
				buffer[cnt + 1] = ptr[pos + 1]; // g
				buffer[cnt + 2] = ptr[pos + 2]; // b
				buffer[cnt + 3] = 0x00;         // a
				cnt += 4;
			}
		}

		//ロックを解除する
		bmp->UnlockBits(bmpDate);

		return buffer;
	}

	// https://msdn.microsoft.com/ja-jp/library/hh394035(v=vs.92).aspx
	// http://qiita.com/gomachan7/items/54d43693f943a0986e95
	unsigned char* Encoder::RGBAtoYUV420Planar(unsigned char *rgba, int width, int height)
	{
		int frameSize = width * height;
		int yIndex = 0;
		int uIndex = frameSize;
		int vIndex = frameSize + (frameSize / 4);
		int r, g, b, y, u, v;
		int index = 0;

		unsigned char* buffer = new unsigned char[width * height * 3 / 2];

		for (int j = 0; j < height; j++)
		{
			for (int i = 0; i < width; i++)
			{
				r = rgba[index * 4 + 0] & 0xff;
				g = rgba[index * 4 + 1] & 0xff;
				b = rgba[index * 4 + 2] & 0xff;
				// a = rgba[index * 4 + 3] & 0xff; unused

				y = (int)(0.257 * r + 0.504 * g + 0.098 * b) + 16;
				u = (int)(0.439 * r - 0.368 * g - 0.071 * b) + 128;
				v = (int)(-0.148 * r - 0.291 * g + 0.439 * b) + 128;

				buffer[yIndex++] = (byte)((y < 0) ? 0 : ((y > 255) ? 255 : y));

				if (j % 2 == 0 && index % 2 == 0)
				{
					buffer[uIndex++] = (byte)((u < 0) ? 0 : ((u > 255) ? 255 : u));
					buffer[vIndex++] = (byte)((v < 0) ? 0 : ((v > 255) ? 255 : v));
				}

				index++;
			}
		}

		return buffer;
	}
}
