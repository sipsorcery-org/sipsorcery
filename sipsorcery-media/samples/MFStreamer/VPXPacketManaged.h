#pragma once

using namespace System;

public ref class VPXPacketManaged
{
public:
	VPXPacketManaged(void * buf, size_t sz, bool isKeyFrame);
	~VPXPacketManaged();

	array<Byte> ^ Buffer;		// Compressed data buffer.
	bool IsKeyFrame;
};

