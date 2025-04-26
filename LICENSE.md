Copyright (c) 2006–2025 Aaron Clauson
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name “SIP Sorcery,” nor “Aaron Clauson,” nor the names of any contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS “AS IS” AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE, ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


DTLS & SRTP Implementation Notice
---------------------------------

Portions of the DTLS/SRTP implementation are derived from:

- **Jitsi SRTP Library**  
  (https://github.com/jitsi/jitsi-srtp) licensed under Apache License 2.0  
- **Minisip Project**  
  (https://github.com/csd/minisip) licensed under LGPL  
- **RestComm Media-Core**  
  (https://github.com/RestComm/media-core/tree/master) licensed under AGPL-3.0

Because of these dependencies, users should assume GPL-style obligations apply (e.g. making source code available on request).  

If you wish to avoid GPL obligations, you may remove the `src/net/DtlsSrtp` directory. This will disable WebRTC’s DTLS/SRTP support (but leave core SIP functionality intact unless SRTP is explicitly required).

As an alternative, you could integrate Cisco’s non-GPL [libsrtp](https://github.com/cisco/libsrtp), which many upstream projects originally forked from.

*Caveat: This notice is provided for informational purposes only and does not constitute legal advice.*

