using ACT.Models;
using ACT.Services;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace ACT.Tests;

public class MongoFileRepositoryTests
{
    private readonly Mock<IMongoDatabase> _mockDatabase;
    private readonly Mock<IMongoCollection<UploadedFile>> _mockCollection;
    private readonly MongoFileRepository _repository;

    public MongoFileRepositoryTests()
    {
        _mockDatabase = new Mock<IMongoDatabase>();
        _mockCollection = new Mock<IMongoCollection<UploadedFile>>();

        _mockDatabase
            .Setup(d => d.GetCollection<UploadedFile>("uploaded_files", null))
            .Returns(_mockCollection.Object);

        _repository = new MongoFileRepository(_mockDatabase.Object);
    }

    [Fact]
    public async Task CreateAsync_ShouldInsertFile()
    {
        var file = new UploadedFile { FileName = "test.txt" };

        await _repository.CreateAsync(file);

        _mockCollection.Verify(c => c.InsertOneAsync(file, null, default), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnFile_WhenExists()
    {
        var file = new UploadedFile { FileName = "test.txt" };
        var cursorMock = new Mock<IAsyncCursor<UploadedFile>>();
        cursorMock.Setup(c => c.Current).Returns(new List<UploadedFile> { file });
        cursorMock
            .SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true))
            .Returns(Task.FromResult(false));

        _mockCollection
            .Setup(c => c.FindAsync(It.IsAny<FilterDefinition<UploadedFile>>(), It.IsAny<FindOptions<UploadedFile, UploadedFile>>(), default))
            .ReturnsAsync(cursorMock.Object);

        var result = await _repository.GetByIdAsync(file.Id);

        Assert.NotNull(result);
        Assert.Equal(file.Id, result.Id);
    }
}
