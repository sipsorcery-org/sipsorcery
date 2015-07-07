#pragma once

using namespace System;

#include "..\include\libsrtp\rtp.h"
#include "..\include\libsrtp\srtp.h"

namespace SIPSorceryMedia {

	public ref class SRTPManaged {
		public:
			SRTPManaged(cli::array<System::Byte>^ key);
			~SRTPManaged();
			int ProtectRTP(cli::array<System::Byte>^ buffer, int length);
			int UnprotectRTP(cli::array<System::Byte>^ buffer, int length);

		private:
			srtp_t * _session;
			srtp_policy_t * _policy;
			System::String^ _key;
	};
}