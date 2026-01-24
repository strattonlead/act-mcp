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
    public async Task GetPackagesStatusAsync_ReturnsCorrectStatuses_ForAllPackages()
    {
        // Arrange
        // Mock output simulating all required packages with mixed statuses
        var mockOutput = "remotes:TRUE\r\njsonlite:TRUE\r\nRcpp:TRUE\r\ndevtools:FALSE\r\nactdata:TRUE\r\nbayesactR:FALSE\r\ninteRact:TRUE";
        var resultWrapper = new RProcessResult(0, mockOutput, "", TimeSpan.Zero, "");
        
        // We verify that the service calls RunStringAsync. 
        // Ideally we should also check that the passed string uses semicolons, but here we just check the parsing logic.
        _mockRunner.Setup(r => r.RunStringAsync(
            It.Is<string>(s => s.Contains("; ")), // Verify semicolon usage
            It.IsAny<string[]>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultWrapper);

        // Act
        var result = await _service.GetPackagesStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(7, result.Count); // Ensure all 7 packages are returned

        Assert.Contains(result, i => i.PackageName == "remotes" && i.IsInstalled);
        Assert.Contains(result, i => i.PackageName == "jsonlite" && i.IsInstalled);
        Assert.Contains(result, i => i.PackageName == "Rcpp" && i.IsInstalled);
        Assert.Contains(result, i => i.PackageName == "devtools" && !i.IsInstalled);
        Assert.Contains(result, i => i.PackageName == "actdata" && i.IsInstalled);
        Assert.Contains(result, i => i.PackageName == "bayesactR" && !i.IsInstalled);
        Assert.Contains(result, i => i.PackageName == "inteRact" && i.IsInstalled);
    }
}
