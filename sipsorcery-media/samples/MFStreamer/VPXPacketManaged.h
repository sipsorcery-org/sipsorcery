#pragma once

using namespace System;

public ref class VPXPacketManaged
{
public:
	VPXPacketManaged(void * buf, size_t sz, bool isKeyFrame, int partitionID);

	array<Byte> ^ Buffer;		// Compressed data buffer.
	int PartitionID;		// The partition id	defines the decoding order of the partitions.Only applicable when "output partition" mode is enabled.First partition has id 0.
	Int64 BriefTimestamp;	// An integer, which when multiplied by the stream's time base, provides the absolute time of a sample.
	UInt64 FrameDuration;	// Duration to show frame (in timebase units).
	bool IsKeyFrame;
};

