using System.Security.Cryptography;
using System.Text;

namespace IdeorAI.Security;

/// <summary>
/// Mascara dados PII antes de gravar em logs.
/// UUIDs de usuário são substituídos por "u:{8-hex}" — rastreável por sessão
/// mas não reversível para o ID real.
/// </summary>
public static class PiiScrubber
{
    /// <summary>
    /// Retorna "u:{primeiros 8 hex do SHA-256}" para um userId.
    /// Produz o mesmo hash para o mesmo ID, permitindo correlacionar logs
    /// sem expor o UUID real.
    /// </summary>
    public static string HashUserId(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return "u:anonymous";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(userId));
        return $"u:{Convert.ToHexString(bytes)[..8].ToLowerInvariant()}";
    }

    /// <summary>
    /// Trunca e mascara um e-mail: "jo**@ex***.com"
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "[no-email]";
        var at = email.IndexOf('@');
        if (at <= 0) return "[masked]";
        var local = email[..at];
        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        var tld = dot > 0 ? domain[dot..] : "";
        var domainBase = dot > 0 ? domain[..dot] : domain;
        var maskedLocal = local.Length <= 2 ? local : $"{local[..2]}{"*".PadRight(Math.Min(local.Length - 2, 4), '*')}";
        var maskedDomain = domainBase.Length <= 2 ? domainBase : $"{domainBase[..2]}{"*".PadRight(Math.Min(domainBase.Length - 2, 3), '*')}";
        return $"{maskedLocal}@{maskedDomain}{tld}";
    }
}
