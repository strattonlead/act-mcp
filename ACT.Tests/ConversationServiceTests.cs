using Xunit;
using ACT.Services;
using ACT.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Moq;

namespace ACT.Tests;

public class ConversationServiceTests
{
    private readonly Mock<IConversationRepository> _mockRepo;
    private readonly Mock<IFileRepository> _mockFileRepo;
    private readonly Mock<IS3Service> _mockS3;
    private readonly Mock<IActProcessingService> _mockProcessing;
    private readonly ConversationService _service;

    public ConversationServiceTests()
    {
        DotNetEnv.Env.TraversePath().Load();
        _mockRepo = new Mock<IConversationRepository>();
        _mockFileRepo = new Mock<IFileRepository>();
        _mockS3 = new Mock<IS3Service>();
        _mockProcessing = new Mock<IActProcessingService>();
        _service = new ConversationService(_mockRepo.Object, _mockFileRepo.Object, _mockS3.Object, _mockProcessing.Object);
        
        // Setup default mock behavior
        _mockProcessing.Setup(p => p.CalculateInteractionAsync(It.IsAny<Interaction>()))
            .ReturnsAsync(new InteractionResult());
    }

    [Fact]
    public async Task Create_AddsNewConversation()
    {
        // Arrange
        var name = "Test Conv";
        var key = "us2001";

        // Act
        var c = await _service.CreateAsync(name, key);
        
        // Assert
        Assert.NotNull(c);
        Assert.Equal(name, c.Name);
        Assert.Equal(key, c.DictionaryKey);
        Assert.NotEqual(Guid.Empty, c.Id);
        
        _mockRepo.Verify(r => r.CreateAsync(It.Is<Conversation>(x => x.Name == name && x.DictionaryKey == key)), Times.Once);
    }

    [Fact]
    public async Task AddPerson_AddsPersonToConversation()
    {
        // Arrange
        var c = new Conversation { Name = "Test", DictionaryKey = "key" };
        _mockRepo.Setup(r => r.GetByIdAsync(c.Id)).ReturnsAsync(c);

        var p = new Person { Name = "Dave", Identity = "student" };
        
        // Act
        await _service.AddPersonAsync(c.Id, p);
        
        // Assert
        Assert.Single(c.Persons);
        Assert.Equal("Dave", c.Persons[0].Name);
        _mockRepo.Verify(r => r.UpdateAsync(c), Times.Once);
    }

    [Fact]
    public async Task AddSituation_AddsSituation()
    {
        // Arrange
        var c = new Conversation { Name = "Test", DictionaryKey = "key" };
        _mockRepo.Setup(r => r.GetByIdAsync(c.Id)).ReturnsAsync(c);

        // Act
        var s = await _service.AddSituationAsync(c.Id, "cooperation");
        
        // Assert
        Assert.Single(c.Situations);
        Assert.Equal("cooperation", s.Type);
        _mockRepo.Verify(r => r.UpdateAsync(c), Times.Once);
    }

    [Fact]
    public async Task AddEvent_AddsEventToSituation()
    {
        // Arrange
        var c = new Conversation { Name = "Test", DictionaryKey = "key" };
        var s = new Situation { Type = "test_sit" };
        c.Situations.Add(s);
        
        _mockRepo.Setup(r => r.GetByIdAsync(c.Id)).ReturnsAsync(c);

        var interaction = new Interaction { Behavior = "smile" };

        // Act
        await _service.AddEventAsync(c.Id, s, interaction);

        // Assert
        Assert.Single(s.Events);
        Assert.Equal("smile", s.Events[0].Behavior);
        _mockRepo.Verify(r => r.UpdateAsync(c), Times.Once);
    }
}
