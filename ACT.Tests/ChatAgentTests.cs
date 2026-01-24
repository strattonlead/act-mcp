using Xunit;
using Moq;
using ACT.Services;
using ACT.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using System;

namespace ACT.Tests;

public class ChatAgentTests
{
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly ChatAgent _agent;

    public ChatAgentTests()
    {
        DotNetEnv.Env.TraversePath().Load();
        _mockChatClient = new Mock<IChatClient>();
        _agent = new ChatAgent(_mockChatClient.Object, NullLogger<ChatAgent>.Instance);
    }

    [Fact]
    public async Task ParseMessagesAsync_ReturnsMessages_WhenLlmReturnsJson()
    {
        // Arrange
        var rawText = "A: Hello\nB: Hi";
        var expectedJson = "[{\"speaker\":\"A\", \"content\":\"Hello\", \"order\":0}, {\"speaker\":\"B\", \"content\":\"Hi\", \"order\":1}]";
        
        // ChatResponse is the return type for GetResponseAsync in MEAI
        var chatResponse = new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, expectedJson) });
        
        _mockChatClient.Setup(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(), 
            It.IsAny<ChatOptions>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        // Act
        var result = await _agent.ParseMessagesAsync(rawText);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].Speaker);
        Assert.Equal("Hello", result[0].Content);
    }

    [Fact]
    public async Task ExtractActEventsAsync_ReturnsTextResponse()
    {
        // The implementation returns raw string from LLM
        // Arrange
        var message = "A kicks B";
        var expectedResponse = "A|kicks|B";
        
        var chatResponse = new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, expectedResponse) });
        
        _mockChatClient.Setup(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(), 
            It.IsAny<ChatOptions>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        // Act
        var result = await _agent.ExtractActEventsAsync(message);

        // Assert
        Assert.Equal("A|kicks|B", result);
    }

    [Fact]
    public async Task DetectSituationsAsync_ReturnsSituations_WhenLlmReturnsJson()
    {
         // Arrange
        var messages = new List<ParsedMessage>();
        var speakers = new List<LabeledSpeaker>();
        var behaviors = new List<string>();

        var expectedJson = "[{\"name\":\"Sit1\", \"events\":[]}]";
        var chatResponse = new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, expectedJson) });

        _mockChatClient.Setup(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(), 
            It.IsAny<ChatOptions>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        // Act
        var result = await _agent.DetectSituationsAsync(messages, speakers, behaviors);

        // Assert
        Assert.Single(result);
        Assert.Equal("Sit1", result[0].Name);
    }
}
