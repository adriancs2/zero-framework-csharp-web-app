using System.Collections.Generic;
using System.Text;

namespace System.engine
{
    // ===== Site-wide date display formatting =====
    // A single place that turns a DateTime into the string shown to visitors and
    // in the admin UI. The format is chosen by the admin in Settings and stored
    // under the "date_format" setting key. Every public/admin "date" display
    // routes through Format() so one setting controls them all.
    //
    // The stored format is a standard .NET custom date/time format string built
    // only from the day/month/year tokens and separators offered in the UI. We
    // re-validate on use so a malformed or empty setting can never throw or leak
    // an unexpected format — it simply falls back to the default.
    public static class DateDisplay
    {
        public const string SettingKey = "date_format";

        // The default applied when the admin has not chosen a format (e.g. a brand
        // new install / fresh database).
        public const string DefaultFormat = "MMM d, yyyy";

        // The day/month/year tokens we present in the UI, longest first so the
        // validator greedily consumes "dddd" before "ddd" before "dd" before "d".
        static readonly string[] Tokens =
        {
            "dddd", "ddd", "dd", "d",
            "MMMM", "MMM", "MM", "M",
            "yyyy", "yy", "y"
        };

        // Separators accepted between tokens: space, comma, dash, dot, slash,
        // backslash. (A backslash in a .NET format string is an escape char, so
        // when present it must be doubled — see SanitizeForToString.)
        static readonly char[] Separators = { ' ', ',', '-', '.', '/', '\\' };

        // The current, validated format string. Reads the setting, validates it,
        // and returns DefaultFormat when it is empty or invalid.
        public static string CurrentFormat
        {
            get
            {
                string raw = Db.GetSetting(SettingKey, "");
                if (IsValid(raw)) return raw;
                return DefaultFormat;
            }
        }

        // Format a DateTime for display using the admin-chosen format.
        public static string Format(DateTime date)
        {
            return date.ToString(SanitizeForToString(CurrentFormat));
        }

        // Format a nullable DateTime; returns "" for null.
        public static string Format(DateTime? date)
        {
            if (date == null) return "";
            return Format(date.Value);
        }

        // True when the string is composed only of the allowed day/month/year
        // tokens and the allowed separators (and is non-empty). Anything else —
        // arbitrary letters, quotes, other format specifiers — is rejected.
        public static bool IsValid(string fmt)
        {
            if (string.IsNullOrEmpty(fmt)) return false;

            int i = 0;
            bool sawToken = false;
            while (i < fmt.Length)
            {
                char c = fmt[i];

                // Separator run.
                if (IsSeparator(c)) { i++; continue; }

                // Otherwise must be the start of a known token.
                string tok = MatchToken(fmt, i);
                if (tok == null) return false;
                sawToken = true;
                i += tok.Length;
            }
            return sawToken;
        }

        // Convert a validated UI format into one safe to hand to DateTime.ToString.
        // The only adjustment needed is escaping any literal backslash separator
        // (a lone '\' is an escape char in .NET format strings). Assumes the input
        // already passed IsValid; if it hasn't, CurrentFormat would have replaced
        // it with the default before we get here.
        static string SanitizeForToString(string fmt)
        {
            if (string.IsNullOrEmpty(fmt)) return DefaultFormat;
            var sb = new StringBuilder(fmt.Length + 4);
            for (int i = 0; i < fmt.Length; i++)
            {
                char c = fmt[i];
                if (c == '\\') sb.Append("\\\\"); // escape literal backslash
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static bool IsSeparator(char c)
        {
            for (int i = 0; i < Separators.Length; i++)
                if (Separators[i] == c) return true;
            return false;
        }

        // Returns the longest token matching at position i, or null if none does.
        static string MatchToken(string fmt, int i)
        {
            for (int t = 0; t < Tokens.Length; t++)
            {
                string tok = Tokens[t];
                if (i + tok.Length > fmt.Length) continue;
                bool ok = true;
                for (int k = 0; k < tok.Length; k++)
                {
                    if (fmt[i + k] != tok[k]) { ok = false; break; }
                }
                // Guard against e.g. matching "d" when the run is actually "ddddd";
                // since Tokens is longest-first this only needs to ensure we don't
                // stop short inside a longer identical-letter run.
                if (ok)
                {
                    char letter = tok[0];
                    int runLen = 0;
                    int j = i;
                    while (j < fmt.Length && fmt[j] == letter) { runLen++; j++; }
                    if (runLen == tok.Length) return tok;
                }
            }
            return null;
        }

        // The token reference shown in the Settings UI.
        public static List<string[]> TokenHelp()
        {
            return new List<string[]>
            {
                new[] { "Day",   "d (1-31), dd (01-31), ddd (Mon), dddd (Monday)" },
                new[] { "Month", "M (1-12), MM (01-12), MMM (Jan), MMMM (January)" },
                new[] { "Year",  "y (0-99), yy (00-99), yyyy (e.g. 2026)" }
            };
        }
    }
}
