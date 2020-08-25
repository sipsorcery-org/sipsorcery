#include <ctime>
#include <iomanip>
#include <iostream>
#include <string>
#include <sstream>

#include "strutils.h"

extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavformat/avio.h>
#include <libavutil/imgutils.h>
#include <libswscale/swscale.h>
#include <libavutil/time.h>
}

#define WIDTH 640
#define HEIGHT 480
#define FRAMES_PER_SECOND 30
#define RTP_OUTPUT_FORMAT "rtp"
#define RTP_URL "rtp://127.0.0.1:5024"
#define ERROR_LEN 128
#define codecID AVCodecID::AV_CODEC_ID_H264 // AVCodecID::AV_CODEC_ID_VP8;

SwsContext* _swsContext;
AVCodec* _codec;
AVCodecContext* _codecCtx;
AVFormatContext* _formatContext;
AVStream* _rtpOutStream;
char _errorLog[ERROR_LEN];
AVCodecParserContext* _parserCtx;
AVCodecContext* _parserCodecCtx;

int main()
{
  std::cout << "FFmpeg Encoder and RTP Stream Test" << std::endl;

  av_log_set_level(AV_LOG_DEBUG);

  // Initialise codec context.
  _codec = avcodec_find_encoder(codecID);
  if (_codec == NULL) {
    throw std::runtime_error("Could not find codec for ID " + std::to_string(codecID) + ".");
  }

  _codecCtx = avcodec_alloc_context3(_codec);
  if (!_codecCtx) {
    std::cerr << "Failed to initialise codec context." << std::endl;;
  }

  _codecCtx->width = WIDTH;
  _codecCtx->height = HEIGHT;
  //_codecCtx->bit_rate = 500000;
  _codecCtx->time_base.den = FRAMES_PER_SECOND;
  _codecCtx->time_base.num = 1;
  //_codecCtx->gop_size = 10;
  //_codecCtx->max_b_frames = 1;
  _codecCtx->pix_fmt = AVPixelFormat::AV_PIX_FMT_YUV420P;

  int res = avcodec_open2(_codecCtx, _codec, NULL);
  if (res < 0) {
    std::cerr << "Failed to open codec: " << av_make_error_string(_errorLog, ERROR_LEN, res) << std::endl;
  }

  // Initialise RTP output stream.
  AVOutputFormat* fmt = av_guess_format(RTP_OUTPUT_FORMAT, NULL, NULL);
  if (!fmt) {
    std::cerr << "Failed to guess output format for " << RTP_OUTPUT_FORMAT << "." << std::endl;
  }

  res = avformat_alloc_output_context2(&_formatContext, fmt, fmt->name, RTP_URL);
  if (res < 0) {
    std::cerr << "Failed to allocate output context: " << av_make_error_string(_errorLog, ERROR_LEN, res) << std::endl;
  }

  _rtpOutStream = avformat_new_stream(_formatContext, _codec);
  if (!_rtpOutStream) {
    std::cerr << "Failed to allocate output stream." << std::endl;
  }

  res = avio_open(&_formatContext->pb, _formatContext->url, AVIO_FLAG_WRITE);
  if (res < 0) {
    std::cerr << "Failed to open RTP output context for writing: " << av_make_error_string(_errorLog, ERROR_LEN, res) << std::endl;
  }

  res = avcodec_parameters_from_context(_rtpOutStream->codecpar, _codecCtx);
  if (res < 0) {
    std::cerr << "Failed to copy codec parameters to stream: " << av_make_error_string(_errorLog, ERROR_LEN, res) << std::endl;
  }

  res = avformat_write_header(_formatContext, NULL);
  if (res < 0) {
    std::cerr << "Failed to write output header: " << av_make_error_string(_errorLog, ERROR_LEN, res) << std::endl;
  }

  av_dump_format(_formatContext, 0, RTP_URL, 1);

  // Set up a parser to extract NAL's from the H264 bit stream.
  // Note this is not needed for sending the RTP (I need to separate the NALs for another reason).
  _parserCtx = av_parser_init(codecID);
  if (!_parserCtx) {
    std::cerr << "Failed to initialise codec parser." << std::endl;
  }

  _parserCodecCtx = avcodec_alloc_context3(NULL);
  if (!_parserCodecCtx) {
    std::cerr << "Failed to initialise parser codec context." << std::endl;
  }

  res = avcodec_parameters_to_context(_parserCodecCtx, _rtpOutStream->codecpar);
  if (res < 0) {
    std::cerr << "Failed to copy codec parameters tp parser: " << av_make_error_string(_errorLog, ERROR_LEN, res) << std::endl;
  }

  //_parserCtx->flags = PARSER_FLAG_COMPLETE_FRAMES;

  // Set a dummy frame with a YUV420 image.
  AVFrame* frame = av_frame_alloc();
  frame->format = AVPixelFormat::AV_PIX_FMT_YUV420P;
  frame->width = WIDTH;
  frame->height = HEIGHT;
  frame->pts = 0;

  res = av_frame_get_buffer(frame, 0);
  if (res < 0) {
    std::cerr << "Failed on av_frame_get_buffer: " << av_make_error_string(_errorLog, ERROR_LEN, res) << std::endl;
  }

  res = av_frame_make_writable(frame);
  if (res < 0) {
    std::cerr << "Failed on av_frame_make_writable: " << av_make_error_string(_errorLog, ERROR_LEN, res) << std::endl;
  }

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

  std::cout << "press any key to start the stream..." << std::endl;
  getchar();

  // Start the loop to encode the static dummy frame and output on the RTP stream.
  AVPacket* pkt = av_packet_alloc();
  //uint8_t data[20000];
  uint8_t* data{ nullptr };
  int dataSize = 0;

  while (true) {
    int sendres = avcodec_send_frame(_codecCtx, frame);
    if (sendres != 0) {
      std::cerr << "avcodec_send_frame error: " << av_make_error_string(_errorLog, ERROR_LEN, sendres) << std::endl;
    }

    // Read encoded packets.
    int ret = 0;
    while (ret >= 0) {

      ret = avcodec_receive_packet(_codecCtx, pkt);

      if (ret == AVERROR(EAGAIN)) {
        // Encoder needs more data.
        break;
      }
      else if (ret < 0) {
        std::cerr << "Failed to encode frame: " << av_make_error_string(_errorLog, ERROR_LEN, sendres) << std::endl;
        break;
      }
      else {
        std::cout << "Encoded packet pts " << pkt->pts << ", size " << pkt->size << "." << std::endl;
        std::cout << toHex(pkt->data, pkt->data + pkt->size) << std::endl;

        int pktOffset = 0;

        // TODO: Find a way to separate the NALs from the Annex B H264 byte stream in the AVPacket data.
        //AVBitStreamFilter 
        
        while (pkt->size > pktOffset) {
          int bytesRead = av_parser_parse2(_parserCtx, _parserCodecCtx, &data, &dataSize, pkt->data + pktOffset, pkt->size - pktOffset, AV_NOPTS_VALUE, AV_NOPTS_VALUE, 0);

          if (bytesRead == 0) {
            std::cout << "Failed to parse data from packet." << std::endl;
            break;
          }
          else if (bytesRead < 0) {
            std::cerr << "av_parser_parse2 error: " << av_make_error_string(_errorLog, ERROR_LEN, bytesRead) << std::endl;
            break;
          }
          else {
            std::cout << "Codec parser bytes read " << bytesRead << ", data size " << dataSize << "." << std::endl;
            pktOffset += bytesRead;
            std::cout << "nal: " << toHex(data, data + dataSize) << "." << std::endl;
          }
        }
      }

      // Write the encoded packet to the RTP stream.
      int sendRes = av_write_frame(_formatContext, pkt);
      if (sendRes < 0) {
        std::cerr << "Failed to write frame to output stream: " << av_make_error_string(_errorLog, ERROR_LEN, sendres) << std::endl;
        break;
      }

      //std::cout << "press any key to continue..." << std::endl;
      //getchar();
    }

    av_usleep(1000000 / FRAMES_PER_SECOND);

    frame->pts++;
  }

  av_packet_free(&pkt);
  av_frame_free(&frame);
  avcodec_close(_codecCtx);
  avcodec_free_context(&_codecCtx);
  avformat_free_context(_formatContext);

  return 0;
}