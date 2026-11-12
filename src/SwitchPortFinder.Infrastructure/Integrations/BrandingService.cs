using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Integrations;

public sealed record BrandingDto(string? CompanyName, string? LinkUrl, bool HasLogo, long Version);

/// <summary>Company name, link and logo. Only PNG, JPEG, GIF and WebP up to 512 KB are accepted (no SVG: it can carry script); the type is checked from the file's own bytes.</summary>
public class BrandingService(AppDbContext db)
{
    public const int MaxLogoBytes = 512 * 1024;

    public static string? ImageType(ReadOnlySpan<byte> b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "image/gif";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }

    public async Task<BrandingDto> GetAsync(CancellationToken ct)
    {
        var b = await db.Brandings.AsNoTracking().Select(x => new { x.CompanyName, x.LinkUrl, Has = x.LogoBytes != null, x.UpdatedAt }).FirstOrDefaultAsync(ct);
        return b is null ? new(null, null, false, 0) : new(b.CompanyName, b.LinkUrl, b.Has, b.UpdatedAt.Ticks);
    }

    public async Task<(byte[] Bytes, string Type)?> LogoAsync(CancellationToken ct)
    {
        var b = await db.Brandings.AsNoTracking().FirstOrDefaultAsync(ct);
        return b?.LogoBytes is { } bytes && b.LogoContentType is { } t ? (bytes, t) : null;
    }

    private async Task<Branding> RowAsync(CancellationToken ct)
    {
        var b = await db.Brandings.FirstOrDefaultAsync(ct);
        if (b is null) db.Brandings.Add(b = new Branding());
        return b;
    }

    public async Task<BrandingDto> SaveAsync(string? name, string? url, CancellationToken ct)
    {
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (name is { Length: > 100 } || name?.Any(char.IsControl) == true) throw new ValidationFailedException("companyName", "The name can be up to 100 characters.");
        string? link = null;
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (url.Trim().Length > 300 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(u.UserInfo)) throw new ValidationFailedException("linkUrl", "Enter a full web address starting with http:// or https://");
            link = u.ToString();
        }
        var b = await RowAsync(ct); b.CompanyName = name; b.LinkUrl = link; b.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<BrandingDto> SetLogoAsync(byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length == 0 || bytes.Length > MaxLogoBytes) throw new ValidationFailedException("logo", "The logo must be an image of up to 512 KB.");
        var type = ImageType(bytes) ?? throw new ValidationFailedException("logo", "Use a PNG, JPEG, GIF or WebP image.");
        var b = await RowAsync(ct); b.LogoBytes = bytes; b.LogoContentType = type; b.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<BrandingDto> RemoveLogoAsync(CancellationToken ct)
    {
        var b = await RowAsync(ct); b.LogoBytes = null; b.LogoContentType = null; b.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }
}
