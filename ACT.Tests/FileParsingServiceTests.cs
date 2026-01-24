using Xunit;
using ACT.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ACT.Tests;

public class FileParsingServiceTests
{
    private readonly FileParsingService _service;

    public FileParsingServiceTests()
    {
        DotNetEnv.Env.TraversePath().Load();
        _service = new FileParsingService(NullLogger<FileParsingService>.Instance);
    }

    [Fact]
    public async Task ExtractTextAsync_ExtractsFromTxt()
    {
        var content = "Hello World";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        
        var result = await _service.ExtractTextAsync(stream, "test.txt");
        
        Assert.Equal("Hello World", result);
    }

    // Testing PDF extraction generally requires a valid PDF binary in the stream.
    // We can try to construct a minimal valid PDF or skip it for unit tests if we don't want to carry binary blobs.
    // For now, testing .txt covers the switch logic for at least one case.
    // Error handling test:

    [Fact]
    public async Task ExtractTextAsync_Throws_OnUnsupportedExtension()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<System.NotSupportedException>(() => 
            _service.ExtractTextAsync(stream, "test.exe")
        );
    }
}
