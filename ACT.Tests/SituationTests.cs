using Xunit;
using ACT.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System;
using System.IO;

namespace ACT.Tests;

public class SituationTests
{
    private readonly ISituationService _situationService;

    public SituationTests()
    {
        _situationService = new SituationService();
    }

    [Fact]
    public void CreateSituation_CreatesEmptySituationByDefault()
    {
        var situation = _situationService.CreateSituation();
        Assert.NotNull(situation);
        Assert.Equal("Empty", situation.Type);
        Assert.Empty(situation.Persons);
    }

    [Fact]
    public void AddPerson_AddsPersonToSituation()
    {
        var situation = _situationService.CreateSituation();
        _situationService.AddPerson(situation, "Alice", "student", "female");

        Assert.Single(situation.Persons);
        var person = situation.Persons[0];
        Assert.Equal("Alice", person.Name);
        Assert.Equal("student", person.Identity);
        Assert.Equal("female", person.Gender);
    }
}

public class ValueVerificationTests
{
    [Fact]
    public async Task VerifyGermany2007_Student_Values()
    {
        // This test runs the R script to get the actual values
        // We need an instance of RScriptRunner. 
        // Note: RScriptRunner depends on having R installed and valid path logic.
        // In a unit test environment, we might need to be careful about paths.
        
        var logger = NullLogger<RScriptRunner>.Instance;
        var runner = new RScriptRunner(logger); 

        // The script is in ACT/Scripts/verify_germany2007.R
        // We need to point to it correctly. 
        // Assuming test execution directory is at bin/Debug/net8.0/
        // and ACT project is at ../../../ACT/
        
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "../../../../ACT/Scripts/verify_germany2007.R");
        scriptPath = Path.GetFullPath(scriptPath);

        if (!File.Exists(scriptPath))
        {
             // Fallback or try to find where it is if running from different context
             // But for now, let's assume standard layout
             Assert.Fail($"Script not found at {scriptPath}");
        }

        // We can't easily use RunJsonAsync because it expects the script to be relative or handle checking.
        // Let's use RunScriptAsync directly if we modify RScriptRunner to allow absolute paths 
        // or just use the logic from RScriptRunner here manually if needed, 
        // BUT RScriptRunner is what we want to test/use.
        
        // Actually RScriptRunner.RunAsync takes a script path.
        // Let's see if we can just pass the absolute path.
        
        var procResult = await runner.RunAsync(scriptPath, args: null);
        var output = procResult.StdOut;
        Assert.False(string.IsNullOrWhiteSpace(output), $"R script output was empty. StdErr: {procResult.StdErr}");
        
        // Parse output
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        
        Assert.True(root.GetProperty("found").GetBoolean(), "Term 'student' not found in dictionary");
        
        var data = root.GetProperty("data");
        // We expect E, P, A values (usually in columns named 'E', 'P', 'A' or similar in the dataframe)
        // The JSON serialization of a single row dataframe might be an array of objects or just an object depending on keys.
        // Let's inspect what we get.
        
        // We just want to dump this to console to "verify" or "reverse engineer" them for the user.
        // xUnit creates output via ITestOutputHelper usually, but let's asserts some presence.
        
        // Depending on actdata structure, columns might be 'femaleMeanE', 'femaleMeanP', etc. or just E, P, A if filtered?
        // Let's Assert that we have *some* data.
        Assert.NotEqual(JsonValueKind.Null, data.ValueKind);
    }
}
