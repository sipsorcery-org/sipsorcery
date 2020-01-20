// ============================================================================
// FileName: PasswordHash.cs
//
// Description:
// This class deals with storing and verifying password hashes.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jan 2012  Aaron Clauson   Created. Borrowed some code snippets from 
//  http://code.google.com/p/stackid/source/browse/OpenIdProvider/Current.cs#1135.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// =============================================================================

using System;
using System.Security.Cryptography;
using System.Text;

namespace SIPSorcery.Sys
{
    public class PasswordHash
    {
        private const int RFC289_MINIMUM_ITERATIONS = 5000;     // The minimum number of iterations to use when deriving the password hash. This slows the algorithm down to help mitigate against brute force and rainbow attacks. 
        private const int SALT_SIZE = 16;

        private static RNGCryptoServiceProvider _randomProvider = new RNGCryptoServiceProvider();

        /// <summary>
        /// Generates a salt that can be used to generate a password hash. The salt is a combination of a block of bytes to represent the 
        /// salt entropy and an integer that represents the iteration count to feed into the RFC289 algorithm used to derive the password hash.
        /// The iterations count is used to slow down the hash generating algorithm to mitigate brute force and rainbow table attacks.
        /// </summary>
        /// <param name="explicitIterations">The number of iterations used to derive the password bytes. Must be greater than the constant specifying the minimum iterations.</param>
        /// <returns>A string it the format iterations.salt.</returns>
        public static string GenerateSalt(int? explicitIterations = null)
        {
            if (explicitIterations.HasValue && explicitIterations.Value < RFC289_MINIMUM_ITERATIONS)
            {
                throw new ArgumentException("Cannot be less than " + RFC289_MINIMUM_ITERATIONS, "explicitIterations");
            }

            byte[] salt = new byte[SALT_SIZE];
            _randomProvider.GetBytes(salt);

            var iterations = (explicitIterations ?? RFC289_MINIMUM_ITERATIONS).ToString("X");

            return iterations + "." + Convert.ToBase64String(salt);
        }

        /// <summary>
        /// Generates the password hash from the password and salt. THe salt must be in the format iterations.salt.
        /// </summary>
        /// <param name="value">The value to generate a hash for.</param>
        /// <param name="salt">The salt (and iteration count) to generate the hash with.</param>
        /// <returns>The hash.</returns>
        public static string Hash(string value, string salt)
        {
            var i = salt.IndexOf('.');
            var iters = int.Parse(salt.Substring(0, i), System.Globalization.NumberStyles.HexNumber);
            salt = salt.Substring(i + 1);

            using (var pbkdf2 = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(value), Convert.FromBase64String(salt), iters))
            {
                var key = pbkdf2.GetBytes(24);

                return Convert.ToBase64String(key);
            }
        }
    }
}
