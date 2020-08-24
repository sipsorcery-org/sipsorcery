#include <ctime>
#include <iomanip>
#include <iostream>
#include <string>
#include <sstream>

extern "C"
{
#include <libavcodec\avcodec.h>
#include <libavformat\avformat.h>
#include <libavformat\avio.h>
#include <libavutil\imgutils.h>
#include <libswscale\swscale.h>
#include <libavutil\time.h>
}

#define WIDTH 640
#define HEIGHT 480
#define FRAMES_PER_SECOND 30

SwsContext* _swsContext;
AVCodec* _codec;
AVCodecContext* _codecCtx;

void InitEncoder(AVCodecID codecID, int width, int height, int fps);
//void GetTestImage(AVFrame* imgframe, int frame_index, int width, int height);

int main()
{
  std::cout << "Ffmpeg VP8 Encode Test" << std::endl;

  //av_log_set_level(AV_LOG_DEBUG);
  av_log_set_level(AV_LOG_INFO);
  //av_log_set_callback(av_log_default_callback);

  InitEncoder(AVCodecID::AV_CODEC_ID_VP8, WIDTH, HEIGHT, FRAMES_PER_SECOND);
  //InitEncoder(AVCodecID::AV_CODEC_ID_H264, WIDTH, HEIGHT, FRAMES_PER_SECOND);
  //InitEncoder(AVCodecID::AV_CODEC_ID_MJPEG, WIDTH, HEIGHT, FRAMES_PER_SECOND);

  //auto avc_class = avcodec_get_class();
  //av_log(&avc_class, AV_LOG_ERROR, "%s");

  int linesz = av_image_get_linesize(AVPixelFormat::AV_PIX_FMT_YUV420P, WIDTH, 0);

  AVFrame* frame = av_frame_alloc();
  frame->format = AVPixelFormat::AV_PIX_FMT_YUV420P;
  frame->width = WIDTH;
  frame->height = HEIGHT;
  frame->pts = 0;

  int getbufferRes = av_frame_get_buffer(frame, 0);
  std::cout << "av_frame_get_buffer result " << getbufferRes << "." << std::endl;

  int makeWritableResult = av_frame_make_writable(frame);
  std::cout << "av_frame_make_writable result " << makeWritableResult << "." << std::endl;

  //GetTestImage(frame, 0, WIDTH, HEIGHT);

  for (int y = 0; y < HEIGHT; y++) {
    for (int x = 0; x < WIDTH; x++) {
      frame->data[0][y * frame->linesize[0] + x] = x + y + 1 * 3;
    }
  }

  for (int y = 0; y < HEIGHT / 2; y++) {
    for (int x = 0; x < WIDTH / 2; x++) {
      frame->data[1][y * frame->linesize[1] + x] = 128 + y + 2;
      frame->data[2][y * frame->linesize[2] + x] = 64 + y + 5;
    }
  }

  int sendres = avcodec_send_frame(_codecCtx, frame);
  if (sendres != 0)
  {
    char errBuf[2048];
    av_strerror(sendres, errBuf, 2048);

    std::cout << "avcodec_send_frame result " << sendres << ", " << errBuf << "." << std::endl;
  }

  AVPacket* pkt = av_packet_alloc();

  int ret = 0;
  while (ret >= 0) {
    ret = avcodec_receive_packet(_codecCtx, pkt);
    if (ret == AVERROR(EAGAIN)) {
      std::cout << "avcodec_receive_packet more data required " << ret << "." << std::endl;
    }
    else if (ret < 0) {
      fprintf(stderr, "Error during encoding\n");
      break;
    }
    else {
      std::cout << "Write packet pts " << pkt->pts << ", size " << pkt->size << "." << std::endl;
      av_packet_unref(pkt);
    }

    frame->pts++;
    sendres = avcodec_send_frame(_codecCtx, frame);
    if (sendres != 0)
    {
      char errBuf[2048];
      av_strerror(sendres, errBuf, 2048);
      std::cout << "avcodec_send_frame result " << sendres << ", " << errBuf << "." << std::endl;
      break;
    }
  }

  av_packet_free(&pkt);
  av_frame_free(&frame);
  avcodec_free_context(&_codecCtx);

  return 0;
}

//void log_callback(void* avcl, int level, const char* fmt, va_list vl)
//{
//	std::cout << "log_callback" << "." << std::endl;
//}

void InitEncoder(AVCodecID codecID, int width, int height, int fps)
{
  _codec = avcodec_find_encoder(codecID);
  if (_codec == NULL) {
    throw std::runtime_error("Could not find codec for ID " + std::to_string(codecID) + ".");
  }

  _codecCtx = avcodec_alloc_context3(_codec);
  _codecCtx->width = width;
  _codecCtx->height = height;
  //_codecCtx->bit_rate = 500000;
  _codecCtx->time_base.den = fps;
  _codecCtx->time_base.num = 1;
  //_codecCtx->framerate.num = fps;
  //_codecCtx->time_base.den = 1;
  //_codecCtx->gop_size = 10;
  //_codecCtx->max_b_frames = 1;

#pragma warning(suppress : 26812)
  _codecCtx->pix_fmt = AVPixelFormat::AV_PIX_FMT_YUV420P;

  int res = avcodec_open2(_codecCtx, _codec, NULL);

  std::cout << "avcodec_open2 result " << res << "." << std::endl;
}


//void GetTestImage(AVFrame* dstframe, int frame_index, int width, int height)
//{
//	static const int RANDOM_SQUARE_SIZE = 50;
//	static const int RANDOM_SQUARE_BOTTOM_RIGHT_OFFSET = 5;
//	static const float FONT_SCALE = 2.0;
//
//	cv::Mat m = cv::Mat(height, width, CV_8UC3, cv::Scalar(0, 255, 0));
//	cv::Mat randSq = m.colRange(width - RANDOM_SQUARE_SIZE - RANDOM_SQUARE_BOTTOM_RIGHT_OFFSET, width - RANDOM_SQUARE_BOTTOM_RIGHT_OFFSET).rowRange(height - RANDOM_SQUARE_SIZE - RANDOM_SQUARE_BOTTOM_RIGHT_OFFSET, height - RANDOM_SQUARE_BOTTOM_RIGHT_OFFSET);
//	cv::randu(randSq, cv::Scalar::all(0), cv::Scalar::all(255));
//
//	auto t = std::time(nullptr);
//	tm localTime;
//	localtime_s(&localTime, const_cast<const time_t *>(&t));
//	std::ostringstream oss;
//	oss << std::put_time(&localTime, "%d-%m-%Y %H-%M-%S");
//	auto str = oss.str();
//
//	cv::putText(m, str.c_str(), { 0, 50 }, cv::HersheyFonts::FONT_HERSHEY_SIMPLEX, FONT_SCALE, CV_RGB(0, 0, 0));
//	cv::putText(m, std::to_string(frame_index), { 100, 200 }, cv::HersheyFonts::FONT_HERSHEY_COMPLEX, FONT_SCALE, CV_RGB(0, 0, 0));
//
//	_swsContext = sws_getCachedContext(_swsContext, width, height, AVPixelFormat::AV_PIX_FMT_BGR24, width, height, AVPixelFormat::AV_PIX_FMT_YUV420P, SWS_FAST_BILINEAR, NULL, NULL, NULL);
//
//	AVFrame * src = av_frame_alloc();
//	src->data[0] = (uint8_t*)m.data;
//	src->linesize[0] = av_image_get_linesize(AVPixelFormat::AV_PIX_FMT_BGR24, width, 0);
//
//	sws_scale(_swsContext, src->data, src->linesize, 0, height, dstframe->data, dstframe->linesize);
//
//	av_frame_free(&src);
//}

