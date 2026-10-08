using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using StaySphere.Application.Abstractions;
using StaySphere.Domain.Common;

namespace StaySphere.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>Blob (Azure Blob Storage / Azurite) or Local (disk, served by the API at /media).</summary>
    public string Provider { get; set; } = "Local";
    public string? ConnectionString { get; set; }
    /// <summary>Public base URL for blobs (CDN / Front Door in Azure, Azurite URL locally).</summary>
    public string? PublicBaseUrl { get; set; }
    public string LocalPath { get; set; } = ".data/media";
    public string LocalPublicBaseUrl { get; set; } = "/media";
}

public sealed class BlobFileStorageService(BlobServiceClient client, IOptions<StorageOptions> options) : IFileStorageService
{
    public async Task<StoredFile> UploadAsync(string container, string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var containerClient = client.GetBlobContainerClient(container);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        var blob = containerClient.GetBlobClient(key);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = "public, max-age=31536000, immutable" },
        }, cancellationToken);
        return new StoredFile(key, GetReadUrl(container, key));
    }

    public async Task<Stream?> DownloadAsync(string container, string key, CancellationToken cancellationToken)
    {
        var blob = client.GetBlobContainerClient(container).GetBlobClient(key);
        if (!await blob.ExistsAsync(cancellationToken)) return null;
        return (await blob.DownloadStreamingAsync(cancellationToken: cancellationToken)).Value.Content;
    }

    public Task DeleteAsync(string container, string key, CancellationToken cancellationToken) =>
        client.GetBlobContainerClient(container).GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public string GetReadUrl(string container, string key) =>
        options.Value.PublicBaseUrl is { Length: > 0 } baseUrl
            ? $"{baseUrl.TrimEnd('/')}/{container}/{key}"
            : client.GetBlobContainerClient(container).GetBlobClient(key).Uri.ToString();
}

public sealed class LocalFileStorageService(IOptions<StorageOptions> options) : IFileStorageService
{
    private string Root => Path.GetFullPath(options.Value.LocalPath);

    public async Task<StoredFile> UploadAsync(string container, string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = SafePath(container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
        return new StoredFile(key, GetReadUrl(container, key));
    }

    public Task<Stream?> DownloadAsync(string container, string key, CancellationToken cancellationToken)
    {
        var path = SafePath(container, key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string container, string key, CancellationToken cancellationToken)
    {
        var path = SafePath(container, key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public string GetReadUrl(string container, string key) => $"{options.Value.LocalPublicBaseUrl.TrimEnd('/')}/{container}/{key}";

    private string SafePath(string container, string key)
    {
        var path = Path.GetFullPath(Path.Combine(Root, container, key));
        if (!path.StartsWith(Root, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid storage key.");
        return path;
    }
}

/// <summary>
/// Validates uploads by magic bytes AND by actually decoding them, enforces size/dimension limits, then re-encodes to
/// JPEG — which strips EXIF/GPS metadata and neutralises polyglot files — and produces a thumbnail.
/// </summary>
public sealed class SkiaImageProcessor(ILogger<SkiaImageProcessor> logger) : IImageProcessor
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxPixels = 40_000_000;
    private const int MaxEdge = 2400;
    private const int ThumbEdge = 480;

    public async Task<Result<ProcessedImage>> ProcessAsync(Stream input, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes) return Error.Validation("image.too_large", "Images must be 10 MB or smaller.");
            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        if (!HasAllowedSignature(bytes)) return Error.Validation("image.type", "Only JPEG, PNG and WebP images are allowed.");

        try
        {
            using var codec = SKCodec.Create(new MemoryStream(bytes));
            if (codec is null) return Error.Validation("image.invalid", "The file is not a valid image.");
            if ((long)codec.Info.Width * codec.Info.Height > MaxPixels) return Error.Validation("image.dimensions", "Image dimensions are too large.");
            if (codec.Info.Width < 320 || codec.Info.Height < 240) return Error.Validation("image.too_small", "Images must be at least 320×240 pixels.");

            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null) return Error.Validation("image.invalid", "The file is not a valid image.");
            var (full, w, h) = Encode(bitmap, MaxEdge, 85);
            var (thumb, _, _) = Encode(bitmap, ThumbEdge, 78);
            return new ProcessedImage(full, thumb, "image/jpeg", ".jpg", w, h);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Image decode failed");
            return Error.Validation("image.invalid", "The file is not a valid image.");
        }
    }

    private static (byte[] Data, int Width, int Height) Encode(SKBitmap source, int maxEdge, int quality)
    {
        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(source.Width, source.Height));
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var resized = scale < 1.0 ? source.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) : source.Copy();
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return (data.ToArray(), w, h);
    }

    public static bool HasAllowedSignature(ReadOnlySpan<byte> b) =>
        (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) ||
        (b.Length > 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) ||
        (b.Length > 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8));
}

/// <summary>
/// Malware-scanning port. In Azure, Defender for Storage scans the quarantine container; locally this is a no-op
/// that only rejects the EICAR test signature so the path can be exercised in tests.
/// </summary>
public sealed class BasicMalwareScanner : IMalwareScanner
{
    private static readonly byte[] Eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR"u8.ToArray();

    public Task<bool> IsCleanAsync(byte[] content, CancellationToken cancellationToken) =>
        Task.FromResult(content.AsSpan().IndexOf(Eicar) < 0);
}
