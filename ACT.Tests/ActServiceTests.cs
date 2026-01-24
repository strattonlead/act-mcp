using Xunit;
using Moq;
using ACT.Services;
using ACT.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;
using System;

namespace ACT.Tests;

public class ActServiceTests
{
    private readonly Mock<IRScriptRunner> _mockRunner;
    private readonly ActService _service;

    public ActServiceTests()
    {
        // Load env var if needed (though ActService mainly uses RScriptRunner which relies on it, 
        // mocking RScriptRunner bypasses that dependency for this unit test)
        DotNetEnv.Env.TraversePath().Load();

        _mockRunner = new Mock<IRScriptRunner>();
        _service = new ActService(_mockRunner.Object, NullLogger<ActService>.Instance);
    }

    [Fact]
    public async Task GetDictionariesAsync_ReturnsList_WhenScriptSucceeds()
    {
        // Arrange
        var expected = new List<ActDictionaryDto>
        {
            new ActDictionaryDto { Key = "us2001", Description = "US 2001" }
        };
        var resultWrapper = new RProcessResult<List<ActDictionaryDto>>(
            new RProcessResult(0, "[]", "", TimeSpan.Zero, ""), 
            expected
        );

        _mockRunner.Setup(r => r.RunJsonAsync<List<ActDictionaryDto>>(
            It.IsAny<string>(), 
            It.IsAny<string[]>(), 
            (JsonSerializerOptions)null,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultWrapper);

        // Act
        var result = await _service.GetDictionariesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("us2001", result[0].Key);
    }

    [Fact]
    public async Task GetDictionaryIdentitiesAsync_ReturnsList_WhenScriptSucceeds()
    {
        // Arrange
        var expected = new List<string> { "student", "teacher" };
        var resultWrapper = new RProcessResult<List<string>>(
            new RProcessResult(0, "[]", "", TimeSpan.Zero, ""), 
            expected
        );

        _mockRunner.Setup(r => r.RunJsonAsync<List<string>>(
            It.Is<string>(s => s.Contains("get_act_identities.R")), 
            It.Is<string[]>(args => args[0] == "testkey"), 
            (JsonSerializerOptions)null,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultWrapper);

        // Act
        var result = await _service.GetDictionaryIdentitiesAsync("testkey");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains("student", result);
    }

    [Fact]
    public async Task GetDictionaryBehaviorsAsync_ReturnsList_WhenScriptSucceeds()
    {
        // Arrange
        var expected = new List<string> { "ask", "answer" };
        var resultWrapper = new RProcessResult<List<string>>(
            new RProcessResult(0, "[]", "", TimeSpan.Zero, ""), 
            expected
        );

        _mockRunner.Setup(r => r.RunJsonAsync<List<string>>(
            It.Is<string>(s => s.Contains("get_act_behaviors.R")), 
            It.Is<string[]>(args => args[0] == "testkey"), 
            (JsonSerializerOptions)null,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultWrapper);

        // Act
        var result = await _service.GetDictionaryBehaviorsAsync("testkey");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains("ask", result);
    }
}
