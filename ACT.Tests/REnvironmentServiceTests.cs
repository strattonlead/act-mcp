using Xunit;
using Moq;
using ACT.Services;
using ACT.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace ACT.Tests;

public class REnvironmentServiceTests
{
    private readonly Mock<IRScriptRunner> _mockRunner;
    private readonly REnvironmentService _service;

    public REnvironmentServiceTests()
    {
        DotNetEnv.Env.TraversePath().Load();
        _mockRunner = new Mock<IRScriptRunner>();
        _service = new REnvironmentService(NullLogger<REnvironmentService>.Instance, _mockRunner.Object);
    }

    [Fact]
    public async Task GetPackagesStatusAsync_ReturnsStatus_FromR()
    {
        // Arrange
        // The service runs a string of R code and expects output "pkg:TRUE" lines.
        var mockOutput = "remotes:TRUE\njsonlite:FALSE";
        var resultWrapper = new RProcessResult(0, mockOutput, "", TimeSpan.Zero, "");
        
        _mockRunner.Setup(r => r.RunStringAsync(
            It.IsAny<string>(), 
            It.IsAny<string[]>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultWrapper);

        // Act
        var result = await _service.GetPackagesStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result, i => i.PackageName == "remotes" && i.IsInstalled);
        Assert.Contains(result, i => i.PackageName == "jsonlite" && !i.IsInstalled);
    }
}
