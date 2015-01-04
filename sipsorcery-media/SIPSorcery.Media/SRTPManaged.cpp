#include <iostream>
#include <msclr/marshal_cppstd.h>
#include "SRTPManaged.h"

using namespace System;
using namespace System::Runtime::InteropServices;

namespace SIPSorceryMedia {

	SRTPManaged::SRTPManaged(cli::array<System::Byte>^ key) : _session()
	{
		pin_ptr<System::Byte> p = &key[0];
		unsigned char* pby = p;
		unsigned char* pch = reinterpret_cast<unsigned char*>(pby);

		_policy = (srtp_policy_t *)malloc(sizeof(srtp_policy_t));

		// initialize libSRTP
		srtp_init();

		// set policy to describe a policy for an SRTP stream
		crypto_policy_set_rtp_default(&_policy->rtp);
		crypto_policy_set_rtcp_default(&_policy->rtcp);

		_policy->key = pch;
		_policy->window_size = 128;
		_policy->allow_repeat_tx = 0;
		_policy->ssrc.type = ssrc_any_outbound;
		_policy->next = NULL;
		_session = new srtp_t();
		auto err = srtp_create(_session, _policy);

		std::cout << "Create srtp session result " << err << "." << std::endl;
	}

	int SRTPManaged::UnprotectRTP(cli::array<System::Byte>^ buffer, int length)
	{
		pin_ptr<System::Byte> p = &buffer[0];
		unsigned char* pby = p;
		char* pch = reinterpret_cast<char*>(pby);

		return srtp_unprotect(*_session, pch, &length);
	}

	int SRTPManaged::ProtectRTP(cli::array<System::Byte>^ buffer, int length)
	{
		pin_ptr<System::Byte> p = &buffer[0];
		unsigned char* pby = p;
		char* pch = reinterpret_cast<char*>(pby);

		return srtp_protect(*_session, pch, &length);
	}
}
