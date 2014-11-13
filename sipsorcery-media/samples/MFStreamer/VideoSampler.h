#pragma once

#include "VPXPacketManaged.h"

namespace SIPSorceryMedia
{
	public ref class VideoSampler
	{
	public:
		VideoSampler();
		~VideoSampler();
		void Init();
		VPXPacketManaged^ GetSample();
	};
}

