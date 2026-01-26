using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IS3Service
{
    Task<string> UploadFileAsync(Stream stream, string fileName, string contentType);
    Task DeleteFileAsync(string bucketName, string objectKey);
}

public class S3Service : IS3Service
{
    private readonly ILogger<S3Service> _logger;
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;

    public S3Service(IMinioClient minioClient, ILogger<S3Service> logger)
    {
        _logger = logger;
        _bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME") ?? "batch-evaluation";
        _minioClient = minioClient;
    }

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType)
    {
        try
        {
            // Ensure bucket exists
            var beArgs = new BucketExistsArgs().WithBucket(_bucketName);
            bool found = await _minioClient.BucketExistsAsync(beArgs);
            if (!found)
            {
                var mbArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _minioClient.MakeBucketAsync(mbArgs);
            }

            // Reset stream position if needed
            if (stream.Position > 0)
                stream.Position = 0;

            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithStreamData(stream)
                .WithObjectSize(stream.Length)
                .WithContentType(contentType);

            await _minioClient.PutObjectAsync(putObjectArgs);

            // Return the object name or a pseudo URL
            // Since this is Minio, we might not have a public URL easily constructed without knowing if it's http/https and port.
            // Returning the object key (fileName) for reference.
            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error uploading {fileName} to S3.");
            throw;
        }
    }

    public async Task DeleteFileAsync(string bucketName, string objectKey)
    {
        try
        {
             var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey);

             await _minioClient.RemoveObjectAsync(removeObjectArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting {objectKey} from {bucketName} in S3.");
            throw;
        }
    }
}

public static class S3ServiceDI
{
    public static void AddS3Service(this IServiceCollection services, ILogger logger)
    {
        var s3Endpoint = Environment.GetEnvironmentVariable("S3_ENDPOINT");
        var s3AccessKey = Environment.GetEnvironmentVariable("S3_ACCESS_KEY");
        var s3SecretKey = Environment.GetEnvironmentVariable("S3_SECRET_KEY");

        if (!string.IsNullOrEmpty(s3Endpoint) && !string.IsNullOrEmpty(s3AccessKey) && !string.IsNullOrEmpty(s3SecretKey))
        {
            logger.LogInformation("Configuring Minio/S3 Client.");
            var secure = s3Endpoint.StartsWith("https");
            var cleanEndpoint = s3Endpoint.Replace("https://", "").Replace("http://", "");

            services.AddSingleton<IMinioClient>(sp =>
            {
                return new MinioClient()
                   .WithEndpoint(cleanEndpoint)
                   .WithCredentials(s3AccessKey, s3SecretKey)
                   .WithSSL(secure)
                   .Build();
            });
        }
        else
        {
            logger.LogWarning("S3 Configuration missing. MinioClient not registered.");
        }
    }
}