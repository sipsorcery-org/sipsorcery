//-----------------------------------------------------------------------------
// Filename: DtlsUtilsUnitTest.cs
//
// Description: Unit tests for the DtlsUtils class.
//
// History:
// 06 Jul 2020	Aaron Clauson	Created.
// 14 Dec 2020  Aaron Clauson   Moved from unit to integration tests..
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.IntegrationTests
{
    [Trait("Category", "integration")]
    public class DtlsUtilsUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public DtlsUtilsUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that creating a new self signed certificate works correctly.
        /// </summary>
        [Fact]
        public void CreateSelfSignedCertifcateUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            (var tlsCert, var pvtKey) = DtlsUtils.CreateSelfSignedTlsCert();

            logger.LogDebug(tlsCert.ToString());

            Assert.NotNull(tlsCert);
            Assert.NotNull(pvtKey);
        }

        /// <summary>
        /// Tests that getting a fingerprint for a certificate works correctly.
        /// </summary>
        [Fact]
        public void GetCertifcateFingerprintUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            (var tlsCert, var pvtKey) = DtlsUtils.CreateSelfSignedTlsCert();
            Assert.NotNull(tlsCert);

            var fingerprint = DtlsUtils.Fingerprint(tlsCert);
            logger.LogDebug($"Fingerprint {fingerprint}.");

            Assert.NotNull(fingerprint.algorithm);
            Assert.NotNull(fingerprint.value);
        }

        /// <summary>
        /// Tests that the secret key can be loaded from a pfx certificate archive file.
        /// </summary>
        /// <remarks>
        /// Fails with netcoreapp on macOS, see https://github.com/dotnet/runtime/issues/23635. Fixed in .NET Core 5, 
        /// see https://github.com/dotnet/corefx/pull/42226. Works with macOS and mono (net461).
        /// </remarks>
        [Fact]
        public void LoadSecretFromArchiveUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

#if NETCOREAPP
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                logger.LogDebug("Test skipped for netcoreapp and macOS as not able to load certificates from a .pfx file pre .NET Core 5.0.");
                return;
            }
#endif
            var cert = new X509Certificate2("certs/localhost.pfx", string.Empty, X509KeyStorageFlags.Exportable);
            Assert.NotNull(cert);
            var key = DtlsUtils.LoadPrivateKeyResource(cert);
            Assert.NotNull(key);
        }

        /// <summary>
        /// Checks that converting a .NET Core framework certificate to a Bouncy Castle certificate can encrypt and decrypt
        /// correctly.
        /// </summary>
        [Fact]
        public void BouncyCertFromCoreFxCert()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

#if NETCOREAPP
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                logger.LogDebug("Test skipped for netcoreapp and macOS as not able to load certificates from a .pfx file pre .NET Core 5.0.");
                return;
            }
#endif

            var coreFxCert = new X509Certificate2("certs/localhost.pfx", string.Empty, X509KeyStorageFlags.Exportable);
            Assert.NotNull(coreFxCert);
            Assert.NotNull(coreFxCert.PrivateKey);

            string coreFxFingerprint = DtlsUtils.Fingerprint(coreFxCert).ToString();
            logger.LogDebug($"Core FX certificate fingerprint {coreFxFingerprint}.");

            var bcCert = Org.BouncyCastle.Security.DotNetUtilities.FromX509Certificate(coreFxCert);
            Assert.NotNull(bcCert);

            var bcKey = Org.BouncyCastle.Security.DotNetUtilities.GetKeyPair(coreFxCert.PrivateKey).Private;
            Assert.NotNull(bcKey);

            string bcFingerprint = DtlsUtils.Fingerprint(bcCert).ToString();
            logger.LogDebug($"BouncyCastle certificate fingerprint {bcFingerprint}.");

            Assert.Equal(coreFxFingerprint, bcFingerprint);
        }
    }
}
