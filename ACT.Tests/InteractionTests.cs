using Xunit;
using ACT.Services;
using ACT.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Tasks;
using System.IO;
using System;
using System.Text.Json;

namespace ACT.Tests;

public class InteractionTests
{
    private readonly ISituationService _situationService;

    public InteractionTests()
    {
        _situationService = new SituationService();
    }

    [Fact]
    public void CreateInteraction_ReturnsValidInteraction()
    {
        var p1 = new Person { Name = "Person 1", Identity = "student" };
        var p2 = new Person { Name = "Person 2", Identity = "assistant" };
        var interaction = _situationService.CreateInteraction(p1, p2, "request something from");

        Assert.NotNull(interaction);
        Assert.Equal("Person 1", interaction.Actor.Name);
        Assert.Equal("Person 2", interaction.Object.Name);
        Assert.Equal("request something from", interaction.Behavior);
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        var p1 = new Person { Name = "Person 1", Identity = "student", Modifier = "_" };
        var p2 = new Person { Name = "Person 2", Identity = "assistant", Modifier = "_" };
        var interaction = _situationService.CreateInteraction(p1, p2, "request something from");

        var formatted = interaction.ToString();
        Assert.Equal("Person 1[_,student],request something from,Person 2[_,assistant]", formatted);
    }
}

public class InteractionVerificationTests
{
    [Fact]
    public async Task VerifyGermany2007_Behavior_Values()
    {
        // Verify "request something from" exists in behavior dataset
        var logger = NullLogger<RScriptRunner>.Instance;
        var runner = new RScriptRunner(logger); 
        
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "../../../../ACT/Scripts/verify_germany2007.R");
        scriptPath = Path.GetFullPath(scriptPath);

        Assert.True(File.Exists(scriptPath), $"Script not found at {scriptPath}");

        // Pass arguments: term="request_something_from" (underscores per dictionary), component="behavior"
        var args = new[] { "request_something_from", "behavior" };
        
        var procResult = await runner.RunAsync(scriptPath, args);
        var output = procResult.StdOut;
        
        Assert.False(string.IsNullOrWhiteSpace(output), $"R script output was empty. StdErr: {procResult.StdErr}");
        
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        
        Assert.True(root.GetProperty("found").GetBoolean(), "Term 'request something from' not found in behavior dictionary");
        
        var data = root.GetProperty("data");
        // Verify we got EPA values (simple check)
        Assert.NotEqual(JsonValueKind.Null, data.ValueKind);
    }

    [Fact]
    public async Task VerifyGermany2007_Student_Values()
    {
        // Re-verify student identity
        var logger = NullLogger<RScriptRunner>.Instance;
        var runner = new RScriptRunner(logger); 
        
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "../../../../ACT/Scripts/verify_germany2007.R");
        scriptPath = Path.GetFullPath(scriptPath);

        // Term defaults to "student", component defaults to "identity" in script if not passed, 
        // but let's pass them explicitly
        var args = new[] { "student", "identity" };

        var procResult = await runner.RunAsync(scriptPath, args);
        var output = procResult.StdOut;
        
        Assert.False(string.IsNullOrWhiteSpace(output), $"R script output was empty. StdErr: {procResult.StdErr}");
        
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        
        Assert.True(root.GetProperty("found").GetBoolean(), "Term 'student' not found in identity dictionary");
    }
}
