using ACT.Components;
using ACT.Services;
using ACT.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Net.Http;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MudBlazor.Services;
using OpenAI.Chat;
using System;
using System.Reflection;

// Load environment variables
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger("Startup");

builder.Services.AddRScriptRunner();
builder.Services.AddScoped<IREnvironmentService, REnvironmentService>();
builder.Services.AddScoped<IActService, ActService>();
builder.Services.AddScoped<ISituationService, SituationService>();
builder.Services.AddScoped<IActProcessingService, ActProcessingService>();
// MongoDB Setup
// Connection String from Env
var mongoConnectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
var mongoDatabaseName = Environment.GetEnvironmentVariable("MONGODB_DATABASE_NAME");

try
{
    BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
}
catch (BsonSerializationException)
{
    // Already registered
}

if (!string.IsNullOrEmpty(mongoConnectionString) && !string.IsNullOrEmpty(mongoDatabaseName))
{
    var mongoClient = new MongoClient(mongoConnectionString);
    var mongoDatabase = mongoClient.GetDatabase(mongoDatabaseName);
    builder.Services.AddSingleton(mongoDatabase);
}
else
{
    Console.WriteLine("WARNING: MongoDB Connection String or Database Name not found.");
}

builder.Services.AddScoped<IConversationRepository, MongoConversationRepository>();
builder.Services.AddScoped<IFileRepository, MongoFileRepository>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IChatAgent, ChatAgent>();
builder.Services.AddActTool(); // Explicitly register ActTool for Controller usage
builder.Services.AddScoped<IAnnotateService, AnnotateService>();
builder.Services.AddSingleton<IActToolMonitor, ActToolMonitor>();
builder.Services.AddSingleton<IActDataCache, ActDataCache>();
builder.Services.AddS3Service(logger);

// Batch Evaluation Services
builder.Services.AddScoped<IS3Service, S3Service>();
builder.Services.AddScoped<IFileParsingService, FileParsingService>();
builder.Services.AddScoped<IBatchEvaluationService, BatchEvaluationService>();
builder.Services.AddSingleton<IBatchFileStatusService, BatchFileStatusService>();
builder.Services.AddScoped<IAutoEvaluationService, AutoEvaluationService>();

// Register Chat Client
var openaiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var chatModel = Environment.GetEnvironmentVariable("CHAT_MODEL");
var ollamaEndpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT");

IChatClient chatClient;
if (!string.IsNullOrEmpty(openaiApiKey))
{
    chatClient = new ChatClient(chatModel ?? "gpt-4o", openaiApiKey).AsIChatClient();
    builder.Services.AddSingleton(sp => new ChatClientBuilder(chatClient).UseFunctionInvocation().Build());
    builder.Services.AddSingleton(new ACT.Services.LlmProviderConfig { IsLocalModel = false });
}
else if (!string.IsNullOrWhiteSpace(ollamaEndpoint) && Uri.IsWellFormedUriString(ollamaEndpoint, UriKind.Absolute))
{
    var ollamaClient = new OllamaSharp.OllamaApiClient(
        new HttpClient { BaseAddress = new Uri(ollamaEndpoint), Timeout = TimeSpan.FromMinutes(30) },
        chatModel);
    chatClient = ollamaClient;
    builder.Services.AddSingleton(sp => new ChatClientBuilder(chatClient).UseFunctionInvocation().Build());
    // Flag for services to use sequential processing with local models
    builder.Services.AddSingleton(new ACT.Services.LlmProviderConfig { IsLocalModel = true });
}
else
{
    // Fallback or warning? For now, we just don't register it, and ActTool might fail if used.
    Console.WriteLine("WARNING: OPENAI_API_KEY not found. IChatAgent will fail if utilized.");
}

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// Register MCP Server
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(Assembly.GetExecutingAssembly());

// Session Tracking
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IMcpSessionService, McpSessionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var useHttps = bool.TryParse(Environment.GetEnvironmentVariable("USE_HTTPS") ?? "false", out var https) ? https : false;
if (useHttps)
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthorization();
app.UseMiddleware<ACT.Middlewares.McpSessionMiddleware>();

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapMcp("/mcp");
app.Run();
