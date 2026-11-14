using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Storage;

public sealed class S3ObjectStorageService : IObjectStorageService, IDisposable
{
    private readonly ObjectStorageSettings _settings;
    private readonly IAmazonS3 _s3Client;
    private readonly bool _ownsClient;

    public S3ObjectStorageService(IOptions<ObjectStorageSettings> options, IAmazonS3? s3Client = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Value;

        if (s3Client is not null)
        {
            _s3Client = s3Client;
            _ownsClient = false;
        }
        else
        {
            AmazonS3Config config = new()
            {
                ForcePathStyle = _settings.ForcePathStyle,
            };

            if (!string.IsNullOrWhiteSpace(_settings.Endpoint))
            {
                config.ServiceURL = _settings.Endpoint;
            }

            BasicAWSCredentials credentials = new(_settings.AccessKey, _settings.SecretKey);
            _s3Client = new AmazonS3Client(credentials, config);
            _ownsClient = true;
        }
    }

    public Task<string> GeneratePresignedUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        GetPreSignedUrlRequest request = new()
        {
            BucketName = _settings.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddMinutes(_settings.PresignedUrlExpiryMinutes),
        };

        string url = _s3Client.GetPreSignedURL(request);
        return Task.FromResult(url);
    }

    public async Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        try
        {
            GetObjectMetadataRequest request = new()
            {
                BucketName = _settings.BucketName,
                Key = objectKey,
            };

            await _s3Client.GetObjectMetadataAsync(request, ct).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public string GetPublicUrl(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        string baseUrl = _settings.PublicCdnBaseUrl.TrimEnd('/');
        string key = objectKey.TrimStart('/');
        return $"{baseUrl}/{key}";
    }

    public async Task<bool> DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        DeleteObjectRequest request = new()
        {
            BucketName = _settings.BucketName,
            Key = objectKey,
        };

        await _s3Client.DeleteObjectAsync(request, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<string> UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        PutObjectRequest request = new()
        {
            BucketName = _settings.BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
        };

        await _s3Client.PutObjectAsync(request, ct).ConfigureAwait(false);
        return GetPublicUrl(objectKey);
    }

    public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        try
        {
            GetObjectRequest request = new()
            {
                BucketName = _settings.BucketName,
                Key = objectKey,
            };

            GetObjectResponse response = await _s3Client.GetObjectAsync(request, ct).ConfigureAwait(false);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _s3Client.Dispose();
        }
    }
}
