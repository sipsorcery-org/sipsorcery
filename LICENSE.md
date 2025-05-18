# License

## 1. BSD 3-Clause "New" or "Revised" License

Copyright (c) 2006–2025 Aaron Clauson
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name “SIP Sorcery,” nor “Aaron Clauson,” nor the names of any contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS “AS IS” AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE, ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

---

## 2. Additional Restriction: Boycott, Divestment, Sanctions (BDS) – Attribution-NonCommercial-ShareAlike

**Boycott Divestment Sanctions – Attribution-NonCommercial-ShareAlike (BDS BY-NC-SA)**

This software **must not be used** to further the Apartheid policies of Israel. Use, modification, or distribution **inside** Israel and the Occupied Territories is strictly forbidden until the demands of the Boycott, Divestment and Sanctions movement have been met:

* Israel has ended the occupation and colonization of all Arab lands occupied in 1967 and dismantled the Wall;
* Arab-Palestinian citizens of Israel have been granted full equality; and
* Palestinian refugees have obtained the right to return to their homes and properties as stipulated in UN Resolution 194.

For all other geographic regions **outside** of Israel and the Occupied Territories, use, modification, and distribution are permitted under the terms of the **BSD 3-Clause "New" or "Revised" License** above (Section 1), provided that any future use, modification, or distribution carries the above BDS restriction and abides by the ShareAlike and NonCommercial principles.

This restriction is **not** intended to limit the rights of Israelis or any other people residing outside of Israel and the Occupied Territories.

In any instance where the BSD 3-Clause License conflicts with the above restriction, the above restriction shall be interpreted as superior, and all other non-conflicting provisions of the BSD 3-Clause license shall remain in effect.

---
## 3. DTLS & SRTP Implementation Notice

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

