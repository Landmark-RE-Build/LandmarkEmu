using System.Globalization;
using System.Text.RegularExpressions;

namespace LandmarkLocal;

/// <summary>Parses progress log lines into a determinate bar value (0–100) or indeterminate.</summary>
public static class StartupProgress
{
    static readonly Regex Percent = new(@"(\d{1,3})\s*%", RegexOptions.Compiled);
    static readonly Regex MbPair = new(@"(\d+)\s*/\s*(\d+)\s*MB", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParse(string line, out double percent, out bool determinate)
    {
        percent = 0;
        determinate = false;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        Match mb = MbPair.Match(line);
        if (mb.Success
            && double.TryParse(mb.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double a)
            && double.TryParse(mb.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double b)
            && b > 0)
        {
            percent = Math.Clamp(a * 100.0 / b, 0, 100);
            determinate = true;
            return true;
        }

        Match pct = Percent.Match(line);
        if (pct.Success
            && double.TryParse(pct.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double p))
        {
            percent = Math.Clamp(p, 0, 100);
            determinate = true;
            return true;
        }

        return false;
    }
}
