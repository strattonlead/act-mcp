using Xunit;
using ACT.Services;
using ACT.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Moq;

namespace ACT.Tests;

public class ConversationFlowTests
{
    private readonly IConversationRepository _repo;
    private readonly ConversationService _service;
    private readonly IMongoCollection<Conversation> _collection;
    private readonly Mock<IFileRepository> _mockFileRepo;
    private readonly Mock<IS3Service> _mockS3;
    private readonly Mock<IActProcessingService> _mockProcessing;

    public ConversationFlowTests()
    {
        DotNetEnv.Env.TraversePath().Load();
        
        var connectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
        var databaseName = Environment.GetEnvironmentVariable("MONGODB_DATABASE_NAME");

        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName))
        {
            throw new InvalidOperationException("MongoDB connection string or database name not found in environment variables.");
        }

        try
        {
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        }
        catch (BsonSerializationException)
        {
            // Already registered
        }

        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        _collection = database.GetCollection<Conversation>("conversations");
        
        _repo = new MongoConversationRepository(database);
        
        _mockFileRepo = new Mock<IFileRepository>();
        _mockS3 = new Mock<IS3Service>();
        _mockProcessing = new Mock<IActProcessingService>();
        _mockProcessing.Setup(x => x.CalculateInteractionAsync(It.IsAny<Interaction>()))
            .ReturnsAsync(new InteractionResult());

        _service = new ConversationService(_repo, _mockFileRepo.Object, _mockS3.Object, _mockProcessing.Object);
    }

    [Fact]
    public async Task CompleteConversationFlow_SavesToRepository()
    {
        // 1. Create formatted conversation
        var name = "Integration Test Conv " + Guid.NewGuid();
        var key = "us2001";
        var createdConv = await _service.CreateAsync(name, key);

        Assert.NotNull(createdConv);
        Assert.Equal(name, createdConv.Name);
        
        // Verify Persistence (Direct DB check)
        var dbConv = await _repo.GetByIdAsync(createdConv.Id);
        Assert.NotNull(dbConv);
        Assert.Equal(name, dbConv.Name);

        try 
        {
            // 2. Add a Person
            var person = new Person { Name = "Alice", Identity = "student" };
            await _service.AddPersonAsync(createdConv.Id, person);

            // Verify Update (Fetch fresh from DB)
            dbConv = await _repo.GetByIdAsync(createdConv.Id);
            Assert.Single(dbConv.Persons);
            Assert.Equal("Alice", dbConv.Persons.First().Name);

            // 3. Add a Situation
            var situation = await _service.AddSituationAsync(createdConv.Id, "cooperation");
            
            // Verify Update
            dbConv = await _repo.GetByIdAsync(createdConv.Id);
            Assert.Single(dbConv.Situations);
            Assert.Equal("cooperation", dbConv.Situations.First().Type);

            // 4. Add an Event to the Situation
            var interaction = new Interaction { Behavior = "greet" };
            await _service.AddEventAsync(createdConv.Id, situation, interaction);

            // Verify Update
            dbConv = await _repo.GetByIdAsync(createdConv.Id);
            var dbSituation = dbConv.Situations.FirstOrDefault(s => s.Id == situation.Id);
            Assert.NotNull(dbSituation);
            Assert.Single(dbSituation.Events);
            Assert.Equal("greet", dbSituation.Events.First().Behavior);
        }
        finally
        {
            // Cleanup: remove the test conversation
            await _collection.DeleteOneAsync(c => c.Id == createdConv.Id);
        }
    }
}
