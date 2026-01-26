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

public class ActSuggestionTests
{
    private readonly Mock<IRScriptRunner> _mockRunner;
    private readonly Mock<IActDataCache> _mockCache;
    private readonly ActService _service;

    public ActSuggestionTests()
    {
        DotNetEnv.Env.TraversePath().Load();

        _mockRunner = new Mock<IRScriptRunner>();
        _mockCache = new Mock<IActDataCache>();

        // Mock Cache Passthrough
        _mockCache.Setup(c => c.GetSuggestionsAsync(It.IsAny<string>(), It.IsAny<Func<Task<List<ActSuggestionDto>>>>()))
            .Returns<string, Func<Task<List<ActSuggestionDto>>>>((k, f) => f());

        _service = new ActService(_mockRunner.Object, _mockCache.Object, NullLogger<ActService>.Instance);
    }

    [Fact]
    public async Task SuggestActionsAsync_ReturnsSuggestions_WhenScriptSucceeds()
    {
        // Arrange
        var expected = new List<ActSuggestionDto>
        {
            new ActSuggestionDto { Term = "advise", Deflection = 1.0, Epa = new List<double>{2.0, 2.0, 2.0} },
            new ActSuggestionDto { Term = "help", Deflection = 1.5, Epa = new List<double>{1.0, 3.0, 2.0} }
        };
        var resultWrapper = new RProcessResult<List<ActSuggestionDto>>(
            new RProcessResult(0, "[]", "", TimeSpan.Zero, ""), 
            expected
        );

        _mockRunner.Setup(r => r.RunJsonAsync<List<ActSuggestionDto>>(
            It.Is<string>(s => s.Contains("suggest_actions.R")), 
            It.Is<string[]>(args => args[0] == "teacher" && args[1] == "student"), 
            (JsonSerializerOptions)null,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultWrapper);

        // Act
        var result = await _service.SuggestActionsAsync("teacher", "student");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("advise", result[0].Term);
        Assert.Equal(1.0, result[0].Deflection);
    }

    [Fact]
    public async Task SuggestActionsAsync_ReturnsEmpty_WhenScriptFails()
    {
         // Arrange
        _mockRunner.Setup(r => r.RunJsonAsync<List<ActSuggestionDto>>(
            It.IsAny<string>(), 
            It.IsAny<string[]>(), 
             (JsonSerializerOptions)null,
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Script failed"));

        // Act
        var result = await _service.SuggestActionsAsync("teacher", "student");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
