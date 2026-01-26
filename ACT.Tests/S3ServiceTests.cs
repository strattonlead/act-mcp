using Xunit;
using Moq;
using ACT.Services;
using Minio;
using Minio.DataModel.Args;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace ACT.Tests;

public class S3ServiceTests
{
    private readonly Mock<IMinioClient> _mockMinio;
    private readonly S3Service _service;

    public S3ServiceTests()
    {
        DotNetEnv.Env.TraversePath().Load();
        // Ensure bucket name is set or default used
        Environment.SetEnvironmentVariable("S3_BUCKET_NAME", "test-bucket");

        _mockMinio = new Mock<IMinioClient>();
        _service = new S3Service(_mockMinio.Object, NullLogger<S3Service>.Instance);
    }

    [Fact]
    public async Task UploadFileAsync_ChecksBucketAndPutsObject()
    {
        // Arrange
        _mockMinio.Setup(m => m.BucketExistsAsync(
            It.IsAny<BucketExistsArgs>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockMinio.Setup(m => m.PutObjectAsync(
            It.IsAny<PutObjectArgs>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((Minio.DataModel.Response.PutObjectResponse)null);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        
        // Act
        var result = await _service.UploadFileAsync(stream, "test.txt", "text/plain");

        // Assert
        Assert.Equal("test.txt", result);
        
        // Verify BucketExists was called
        _mockMinio.Verify(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()), Times.Once);
        // Verify PutObject was called
        _mockMinio.Verify(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteFileAsync_ShouldRemoveObject()
    {
         // Arrange
         _mockMinio.Setup(m => m.RemoveObjectAsync(
             It.IsAny<RemoveObjectArgs>(),
             It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

         // Act
         await _service.DeleteFileAsync("test-bucket", "test.txt");

         // Assert
         _mockMinio.Verify(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
