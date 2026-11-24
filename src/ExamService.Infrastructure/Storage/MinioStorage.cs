using ExamService.Application.Common;
using Minio;
using Minio.DataModel.Args;

namespace ExamService.Infrastructure.Storage;

public sealed class MinioStorage : IObjectStorage
{
    private readonly IMinioClient _client;
    private readonly IMinioClient _signing;
    private readonly string _bucket;

    public MinioStorage(string endpoint, string access, string secret, string bucket, string publicEndpoint)
    {
        _bucket = bucket;
        _client = new MinioClient().WithEndpoint(endpoint).WithCredentials(access, secret).WithSSL(false).Build();
        var uri = new Uri(publicEndpoint.TrimEnd('/'));
        _signing = new MinioClient().WithEndpoint(uri.Authority).WithCredentials(access, secret)
            .WithSSL(uri.Scheme == Uri.UriSchemeHttps).Build();
    }

    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        if (!await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct))
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
    }

    public Task<string> PresignUploadAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct)
        => _signing.PresignedPutObjectAsync(new PresignedPutObjectArgs()
            .WithBucket(_bucket).WithObject(key).WithExpiry((int)expiry.TotalSeconds));

    public Task<string> PresignDownloadAsync(string key, TimeSpan expiry, CancellationToken ct)
        => _signing.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_bucket).WithObject(key).WithExpiry((int)expiry.TotalSeconds));

    public Task RemoveAsync(string key, CancellationToken ct)
        => _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_bucket).WithObject(key), ct);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await _client.StatObjectAsync(new StatObjectArgs().WithBucket(_bucket).WithObject(key), ct);
            return true;
        }
        catch { return false; }
    }
}
