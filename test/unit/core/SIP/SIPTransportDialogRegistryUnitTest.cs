//-----------------------------------------------------------------------------
// Filename: SIPTransportDialogRegistryUnitTest.cs
//
// Description: Unit tests for the SIPTransport dialog owner registry.
//
// Author(s):
// Contributors
//
// History:
// 16 Feb 2026  Contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading.Tasks;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "DialogRegistry")]
    public class SIPTransportDialogRegistryUnitTest
    {
        /// <summary>
        /// A minimal ISIPDialogOwner for testing registry operations.
        /// </summary>
        private class StubDialogOwner : ISIPDialogOwner
        {
            public string DialogCallID { get; set; }
            public string DialogLocalTag { get; set; }
            public string DialogRemoteTag { get; set; }

            public Task OnDialogRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void RegisterDialogOwnerSucceeds()
        {
            var transport = new SIPTransport(true);
            var owner = new StubDialogOwner { DialogCallID = "call-1" };

            bool result = transport.RegisterDialogOwner("call-1", owner);

            Assert.True(result);
            transport.Shutdown();
        }

        [Fact]
        public void DuplicateRegisterRejected()
        {
            var transport = new SIPTransport(true);
            var owner1 = new StubDialogOwner { DialogCallID = "call-1" };
            var owner2 = new StubDialogOwner { DialogCallID = "call-1" };

            transport.RegisterDialogOwner("call-1", owner1);
            bool result = transport.RegisterDialogOwner("call-1", owner2);

            Assert.False(result);
            transport.Shutdown();
        }

        [Fact]
        public void UnregisterDialogOwnerSucceeds()
        {
            var transport = new SIPTransport(true);
            var owner = new StubDialogOwner { DialogCallID = "call-1" };

            transport.RegisterDialogOwner("call-1", owner);
            bool result = transport.UnregisterDialogOwner("call-1", owner);

            Assert.True(result);

            // Should be able to re-register after unregister.
            bool reRegister = transport.RegisterDialogOwner("call-1", owner);
            Assert.True(reRegister);
            transport.Shutdown();
        }

        [Fact]
        public void UnregisterWrongOwnerRejected()
        {
            var transport = new SIPTransport(true);
            var owner1 = new StubDialogOwner { DialogCallID = "call-1" };
            var owner2 = new StubDialogOwner { DialogCallID = "call-1" };

            transport.RegisterDialogOwner("call-1", owner1);
            bool result = transport.UnregisterDialogOwner("call-1", owner2);

            Assert.False(result);
            transport.Shutdown();
        }

        [Fact]
        public void RegisterNullCallIDReturnsFalse()
        {
            var transport = new SIPTransport(true);
            var owner = new StubDialogOwner();

            Assert.False(transport.RegisterDialogOwner(null, owner));
            Assert.False(transport.RegisterDialogOwner("", owner));
            Assert.False(transport.RegisterDialogOwner("call-1", null));

            transport.Shutdown();
        }

        [Fact]
        public void ShutdownClearsRegistry()
        {
            var transport = new SIPTransport(true);
            var owner = new StubDialogOwner { DialogCallID = "call-1" };

            transport.RegisterDialogOwner("call-1", owner);
            transport.Shutdown();

            // After shutdown, a new transport should accept the same Call-ID.
            var transport2 = new SIPTransport(true);
            Assert.True(transport2.RegisterDialogOwner("call-1", owner));
            transport2.Shutdown();
        }

        [Fact]
        public void RegisterIsCaseInsensitive()
        {
            var transport = new SIPTransport(true);
            var owner = new StubDialogOwner { DialogCallID = "Call-ABC" };

            transport.RegisterDialogOwner("Call-ABC", owner);

            // Different case should collide (same key).
            var owner2 = new StubDialogOwner { DialogCallID = "call-abc" };
            Assert.False(transport.RegisterDialogOwner("call-abc", owner2));

            transport.Shutdown();
        }
    }
}
