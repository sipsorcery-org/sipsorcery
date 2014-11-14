#include "stdafx.h"
#include "VideoSampler.h"
#include "MFStreamer.h"
#include "vpx/vpx_encoder.h"

namespace SIPSorceryMedia
{
	VideoSampler::VideoSampler()
	{
	}

	void VideoSampler::Init()
	{
		InitMFStreamer();
	}

	VPXPacketManaged^ VideoSampler::GetSample()
	{
		const vpx_codec_cx_pkt_t *pkt = NULL;
		long res = GetSampleFromMFStreamer(pkt);

		if (res == 0 && pkt != NULL)
		{
			//printf("Got native sample, data length %i.\n", pkt->data.frame.sz);
			return gcnew VPXPacketManaged(pkt->data.frame.buf, pkt->data.frame.sz, pkt->data.frame.flags & VPX_FRAME_IS_KEY);

			delete pkt->data.frame.buf;
			delete pkt;
		}
	}

	VideoSampler::~VideoSampler()
	{
	}
}
