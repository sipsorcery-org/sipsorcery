#include "VPXPacketManaged.h"

using namespace System;
using namespace System::Runtime::InteropServices;

VPXPacketManaged::VPXPacketManaged(void * buf, size_t sz, bool isKeyFrame, int partitionID)
{
	Buffer = gcnew array<Byte>(sz);

	// convert any native pointer to IntPtr by doing C-Style cast
	Marshal::Copy((IntPtr)buf, Buffer, 0, sz);

	IsKeyFrame = isKeyFrame;
	PartitionID = partitionID;
}
