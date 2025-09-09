using System;
using System.Security.Cryptography;
using System.Text;

namespace SCOdyssey.Net
{
    public static class Pkce
    {
        private const string Allowed = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";

        public static string GenerateCodeVerifier(int length = 64)
        {
            if (length < 43 || length > 128)
                throw new ArgumentOutOfRangeException(nameof(length));

            var bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);

            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                sb.Append(Allowed[bytes[i] % Allowed.Length]);
            }
            return sb.ToString();
        }
    }
}
