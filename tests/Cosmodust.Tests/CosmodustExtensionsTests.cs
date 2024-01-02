using Cosmodust.Cosmos.Tests.Domain.Accounts;
using Cosmodust.Extensions;
using Cosmodust.Json;
using Cosmodust.Memory;
using Cosmodust.Serialization;
using Cosmodust.Session;
using Cosmodust.Store;
using Cosmodust.Tests.Domain.Accounts;
using Cosmodust.Tests.Fixtures;
using Cosmodust.Tracking;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cosmodust.Tests;

public class CosmodustExtensionsTests : IClassFixture<CosmosTextFixture>
{
    private readonly CosmosTextFixture _configurationTextFixture;

    public CosmodustExtensionsTests(CosmosTextFixture configurationTextFixture)
    {
        _configurationTextFixture = configurationTextFixture;
    }

    [Fact]
    [Trait("gh_bugfix", "105")]
    public void Ensure_All_Services_Are_Registered_During_Startup()
    {
        var connectionString = _configurationTextFixture.Configuration["COSMOSDB_CONNECTIONSTRING"];
        
        Ensure.NotNullOrWhiteSpace(connectionString, "connection string not configured.");
        
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<QueryFacade>>(NullLogger<QueryFacade>.Instance);

        services.AddCosmodust(
            options =>
            {
                options.WithConnectionString(connectionString)
                    .WithDatabase("database")
                    .WithModel(modelBuilder =>
                    {
                        modelBuilder.DefineEntity<AccountPlan>()
                            .WithId(e => e.Id)
                            .WithPartitionKey(
                                e => e.Id,
                                "ownerId")
                            .ToContainer("todo");
                    })
                    .WithQueryOptions(queryOptions =>
                    {
                        queryOptions.ExcludeCosmosMetadata = true;
                        queryOptions.IncludeETag = true;
                        queryOptions.IndentJsonOutput = false;
                        queryOptions.RenameDocumentCollectionProperties = true;
                    });
            });

        var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetService<CosmodustOptions>().Should().NotBeNull(
            because: "cosmodust options should be registered.");

        serviceProvider.GetService<IMemoryStreamProvider>().Should().NotBeNull(
            because: "the memory stream provider should be registered.");
        serviceProvider.GetService<ShadowPropertyProvider>().Should().NotBeNull(
            because: "the json property provider provider should be registered.");
        serviceProvider.GetService<EntityConfigurationProvider>().Should().NotBeNull(
            because: "the entity configuration provider should be registered.");
        serviceProvider.GetService<SqlParameterObjectTypeResolver>().Should().NotBeNull(
            because: "the sql parameter object type resolver should be registered.");
        serviceProvider.GetService<CosmodustJsonSerializer>().Should().NotBeNull(
            because: "the cosmodust json serializer should be registered.");

        serviceProvider.GetService<CosmodustJsonOptions>().Should().NotBeNull(
            because: "the cosmodust json options should be registered.");

        serviceProvider.GetService<CosmosClient>().Should().NotBeNull(
            because: "the cosmos db client should be registered.");
        serviceProvider.GetService<DocumentStore>().Should().NotBeNull(
            because: "the document store should be registered.");
        serviceProvider.GetService<DocumentSession>().Should().NotBeNull(
            because: "the document session should be registered.");
        serviceProvider.GetService<QueryFacade>().Should().NotBeNull(
            because: "the query facade should be registered.");

        serviceProvider.GetService<ILogger<QueryFacade>>().Should().NotBeNull(
            because: "the query facade's logger should be registered.");
    }
    
    [Fact]
    [Trait("gh_bugfix", "111")]
    public async Task Foo()
    {
        var connectionString = _configurationTextFixture.Configuration["COSMOSDB_CONNECTIONSTRING"];
        
        Ensure.NotNullOrWhiteSpace(connectionString, "connection string not configured.");
        
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<QueryFacade>>(NullLogger<QueryFacade>.Instance);

        services.AddCosmodust(
            options =>
            {
                options.WithConnectionString(connectionString)
                    .WithDatabase("reminderdb")
                    .WithModel(modelBuilder =>
                    {
                        modelBuilder.DefineEntity<Username>()
                            .WithId(e => e.Value)
                            .WithPartitionKey(
                                e => "account",
                                "partitionKey")
                            .ToContainer("accounts");
                    }).WithJsonOptions(jsonOptions =>
                    {
                        jsonOptions.CamelCase();
                        jsonOptions.SerializePrivateFields();
                        jsonOptions.SerializePrivateProperties();
                        jsonOptions.SerializeEntityTypeInfo();
                    });
            });

        var serviceProvider = services.BuildServiceProvider();

        var store = serviceProvider.GetService<DocumentStore>();
        var session = store.CreateSession();

        var value = await session.FindAsync<Username>("mg_scott", "account");

        value.Should().NotBeNull();
    }
}
