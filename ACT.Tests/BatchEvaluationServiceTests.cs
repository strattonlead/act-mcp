using Xunit;
using Moq;
using ACT.Services;
using ACT.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System;

namespace ACT.Tests;

public class BatchEvaluationServiceTests
{
    private readonly Mock<IS3Service> _mockS3;
    private readonly Mock<IFileParsingService> _mockFileParsing;
    private readonly Mock<IChatAgent> _mockChatAgent;
    private readonly Mock<IConversationService> _mockConversation;
    private readonly Mock<IActService> _mockActService;
    private readonly Mock<IActProcessingService> _mockActProcessing;
    
    private readonly BatchEvaluationService _service;

    public BatchEvaluationServiceTests()
    {
        DotNetEnv.Env.TraversePath().Load();

        _mockS3 = new Mock<IS3Service>();
        _mockFileParsing = new Mock<IFileParsingService>();
        _mockChatAgent = new Mock<IChatAgent>();
        _mockConversation = new Mock<IConversationService>();
        _mockActService = new Mock<IActService>();
        _mockActProcessing = new Mock<IActProcessingService>();

        _service = new BatchEvaluationService(
            _mockS3.Object,
            _mockFileParsing.Object,
            _mockChatAgent.Object,
            _mockConversation.Object,
            _mockActService.Object,
            _mockActProcessing.Object,
            NullLogger<BatchEvaluationService>.Instance
        );
    }

    [Fact]
    public async Task AddFilesAsync_AddsFilesToInternalList_PendingState()
    {
        // Arrange
        using var ms = new MemoryStream();
        var browserFile = new Mock<Microsoft.AspNetCore.Components.Forms.IBrowserFile>();
        browserFile.Setup(f => f.Name).Returns("test.txt");
        browserFile.Setup(f => f.Size).Returns(100);
        browserFile.Setup(f => f.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(ms);

        // Act
        await _service.AddFilesAsync(new[] { browserFile.Object });

        // Assert
        var files = _service.GetFiles();
        Assert.Single(files);
        Assert.Equal("test.txt", files[0].FileName);
        Assert.Equal(BatchFileState.Pending, files[0].State);
        Assert.Null(files[0].ExtractedText);
    }

    [Fact]
    public async Task StartProcessingAsync_ProcessesPendingFiles()
    {
        // Arrange
        using var ms = new MemoryStream();
        var browserFile = new Mock<Microsoft.AspNetCore.Components.Forms.IBrowserFile>();
        browserFile.Setup(f => f.Name).Returns("test.txt");
        browserFile.Setup(f => f.OpenReadStream(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(ms);

        await _service.AddFilesAsync(new[] { browserFile.Object });

        _mockActService.Setup(s => s.GetDictionaryIdentitiesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "student" });
        _mockActService.Setup(s => s.GetDictionaryBehaviorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ask" });

        _mockFileParsing.Setup(f => f.ExtractTextAsync(It.IsAny<Stream>(), "test.txt"))
            .ReturnsAsync("User: Hello");
            
        _mockChatAgent.Setup(c => c.ParseMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParsedMessage>()); // Empty list is fine for flow check
            
        _mockChatAgent.Setup(c => c.LabelIdentitiesAsync(It.IsAny<List<ParsedMessage>>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LabeledSpeaker>());
            
        _mockChatAgent.Setup(c => c.DetectSituationsAsync(It.IsAny<List<ParsedMessage>>(), It.IsAny<List<LabeledSpeaker>>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DetectedSituation>());
            
        _mockConversation.Setup(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new Conversation { Id = Guid.NewGuid() });

        // Act
        await _service.StartProcessingAsync("us2001");

        // Assert
        var files = _service.GetFiles();
        Assert.Equal(BatchFileState.Completed, files[0].State);
        Assert.Equal("User: Hello", files[0].ExtractedText);
        
        // Verify dependency calls
        _mockS3.Verify(s => s.UploadFileAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<string>()), Times.Once);
    }
}
