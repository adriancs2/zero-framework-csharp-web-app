using System;
using System.Security.Cryptography;
using System.Text;

namespace System.engine
{
    // PBKDF2 password hashing - stored as "iter.salt.hash" base64.
    public static class Auth
    {
        const int SaltBytes = 16;
        const int HashBytes = 32;
        const int Iterations = 50000;

        public static string Hash(string password)
        {
            if (password == null) password = "";
            byte[] salt = new byte[SaltBytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations))
            {
                byte[] hash = pbkdf2.GetBytes(HashBytes);
                return Iterations + "." + Convert.ToBase64String(salt) + "." + Convert.ToBase64String(hash);
            }
        }

        public static bool Verify(string password, string stored)
        {
            if (password == null) password = "";
            if (string.IsNullOrEmpty(stored)) return false;
            string[] parts = stored.Split('.');
            if (parts.Length != 3) return false;
            int iter;
            if (!int.TryParse(parts[0], out iter)) return false;
            try
            {
                byte[] salt = Convert.FromBase64String(parts[1]);
                byte[] expected = Convert.FromBase64String(parts[2]);

                using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iter))
                {
                    byte[] actual = pbkdf2.GetBytes(expected.Length);
                    return ConstantTimeEquals(actual, expected);
                }
            }
            catch { return false; }
        }

        static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string Slugify(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            bool lastDash = false;
            foreach (char ch in text.ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                {
                    sb.Append(ch);
                    lastDash = false;
                }
                else if (!lastDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
            return sb.ToString().Trim('-');
        }
    }
}
