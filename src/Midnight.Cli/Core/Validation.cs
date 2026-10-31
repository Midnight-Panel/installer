using System.Net;
using System.Text.RegularExpressions;

namespace Midnight.Cli.Core;

public static partial class Validation
{
    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.IgnoreCase)]
    private static partial Regex DomainRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex("^[a-zA-Z0-9_.-]{3,32}$")]
    private static partial Regex UsernameRegex();

    public static bool IsDomain(string value) => DomainRegex().IsMatch(value);

    public static bool IsIpv4(string value)
        => IPAddress.TryParse(value, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && value.Count(c => c == '.') == 3;

    /// <summary>With SSL a domain is required (certificates need one), without SSL an ip or a domain</summary>
    public static bool IsAddress(string value, bool ssl) => ssl ? IsDomain(value) : IsDomain(value) || IsIpv4(value);

    public static bool IsEmail(string value) => EmailRegex().IsMatch(value);

    public static bool IsUsername(string value) => UsernameRegex().IsMatch(value);

    public static bool IsPort(int value) => value is >= 1 and <= 65535;

    /// <summary>"Max.Mustermann+test@example.com" becomes "max.mustermann-test", same rule as the panel</summary>
    public static string UsernameFromEmail(string email)
    {
        var local = email.Split('@')[0];
        var cleaned = new string(local.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-', '.');

        if (cleaned.Length < 3)
            cleaned = "admin";

        return cleaned.Length > 32 ? cleaned[..32] : cleaned;
    }
}
