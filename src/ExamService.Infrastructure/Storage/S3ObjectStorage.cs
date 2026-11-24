using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using ExamService.Application.Common;

namespace ExamService.Infrastructure.Storage;

/// <summary>AWS S3 implementation. Presigned URLs are signed with the real bucket host.</summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public S3ObjectStorage(string region, string bucket, string? accessKey, string? secretKey)
    {
        _bucket = bucket;
        var config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        _client = string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(accessKey, secretKey, config);
    }

    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        var buckets = await _client.ListBucketsAsync(ct);
        if (!buckets.Buckets.Any(b => b.BucketName == _bucket))
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
    }

    public Task<string> PresignUploadAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(_client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket, Key = key, Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(expiry)
        }));

    public Task<string> PresignDownloadAsync(string key, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(_client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket, Key = key, Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry)
        }));

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try { await _client.GetObjectMetadataAsync(_bucket, key, ct); return true; }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return false; }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
        => await _client.DeleteObjectAsync(_bucket, key, ct);

    public async Task PutAsync(string key, byte[] data, string contentType, CancellationToken ct = default)
    {
        await using var stream = new MemoryStream(data);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket, Key = key, InputStream = stream, ContentType = contentType
        }, ct);
    }

    public void Dispose() => _client.Dispose();
}
