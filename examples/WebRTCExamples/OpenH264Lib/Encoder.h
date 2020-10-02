// OpenH264Lib.h

#pragma once

using namespace System;

namespace OpenH264Lib {

	public ref class Encoder
	{
	private:
		int num_of_frames;
		int keyframe_interval;
		int buffer_size;
		unsigned char *i420_buffer;

		// マネージコードのクラスはネイティブコードのクラスをメンバに持つことはできない。その代わりにポインタを使う。
		// コンストラクタでオブジェクトを生成し、ファイナライザで解放する。デストラクタでは解放せずファイナライザを呼び出すだけ。(必ず呼ばれるとは限らないため)
		// これらのポインタはマネージヒープ上に存在するため、＆演算子でアドレスを取得することはできない。pin_ptrを使ってアドレスを固定して呼び出す必要がある。
		ISVCEncoder* encoder;
		SSourcePicture* pic;
		SFrameBSInfo* bsi;

	private:
		typedef int(__stdcall *WelsCreateSVCEncoderFunc)(ISVCEncoder** ppEncoder);
		WelsCreateSVCEncoderFunc CreateEncoderFunc;
		typedef void(__stdcall *WelsDestroySVCEncoderFunc)(ISVCEncoder* ppEncoder);
		WelsDestroySVCEncoderFunc DestroyEncoderFunc;

	private:
		~Encoder(); // デストラクタ
		!Encoder(); // ファイナライザ
	public:
		Encoder(String ^dllName);

	public:
		enum class FrameType { Invalid, IDR, I, P, Skip, IPMixed };
		delegate void OnEncodeCallback(array<Byte> ^data, int length, FrameType keyFrame);
		int Setup(int width, int height, int bps, float fps, float keyFrameInterval, OnEncodeCallback ^onEncode);

		[Obsolete("timestamp argument is unnecessary. use Encode(Bitmap) instead.")]
		int Encode(System::Drawing::Bitmap ^bmp, float timestamp);

		int Encode(System::Drawing::Bitmap ^bmp);
		int Encode(array<Byte> ^i420);
		int Encode(unsigned char *i420);

	private:
		void OnEncode(const SFrameBSInfo% info);
		OnEncodeCallback^ OnEncodeFunc;

	private:
		static unsigned char* BitmapToRGBA(System::Drawing::Bitmap^ bmp, int width, int height);
		static unsigned char* RGBAtoYUV420Planar(unsigned char *rgba, int width, int height);
	};
}
