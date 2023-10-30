using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cosmodust.Cosmos;
using Cosmodust.Cosmos.Json;
using Cosmodust.Json;
using Cosmodust.Samples.TodoApp.Domain;
using Cosmodust.Samples.TodoApp.Infra;
using Cosmodust.Serialization;
using Cosmodust.Session;
using Cosmodust.Store;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new ShadowPropertyStore());
builder.Services.AddSingleton(new EntityConfigurationHolder());
builder.Services.AddSingleton(sp =>
{
    var jsonTypeInfoResolver = new DefaultJsonTypeInfoResolver();

    foreach (var action in new IJsonTypeModifier[]
             {
                 new BackingFieldJsonTypeModifier(sp.GetRequiredService<EntityConfigurationHolder>()),
                 new PropertyJsonTypeModifier(sp.GetRequiredService<EntityConfigurationHolder>()),
                 new PartitionKeyJsonTypeModifier(sp.GetRequiredService<EntityConfigurationHolder>()),
                 new ShadowPropertyJsonTypeModifier(sp.GetRequiredService<EntityConfigurationHolder>()),
                 new TypeMetadataJsonTypeModifier()
             })
    {
        jsonTypeInfoResolver.Modifiers.Add(action.Modify);
    }

    return new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = jsonTypeInfoResolver
    };
});

builder.Services.AddSingleton(sp =>
{
    var client = new CosmosClient(
        sp.GetRequiredService<IConfiguration>()["ConnectionStrings:CosmosDB"],
        new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Direct,
            Serializer = new CosmosJsonSerializer(sp.GetRequiredService<JsonSerializerOptions>())
        });

    return client;
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<CosmosClient>();
    var database = new CosmosDatabase(client.GetDatabase("reminderdb"));

    var store = new DocumentStore(
        database,
        sp.GetRequiredService<JsonSerializerOptions>(),
        sp.GetRequiredService<EntityConfigurationHolder>(),
        sqlParameterCache: sp.GetRequiredService<SqlParameterCache>(),
        shadowPropertyStore: sp.GetRequiredService<ShadowPropertyStore>());

    store.BuildModel(modelBuilder =>
    {
        modelBuilder.HasEntity<Account>()
            .HasId(e => e.Id)
            .HasPartitionKey(
                e => e.Id,
                "ownerId")
            .ToContainer("todo");

        modelBuilder.HasEntity<TodoList>()
            .HasId(e => e.Id)
            .HasPartitionKey(e => e.OwnerId)
            .HasProperty("Items")
            .HasShadowProperty<string>("_etag")
            .HasShadowProperty<long>("_ts")
            .ToContainer("todo");

        modelBuilder.HasEntity<TodoItem>()
            .HasId(e => e.Id)
            .HasPartitionKey(e => e.OwnerId)
            .ToContainer("todo");

        modelBuilder.HasValueObject<ArchiveState>()
            .HasValueObject<TodoItemCompletedState>()
            .HasValueObject<TodoItemPriority>();
    });

    return store;
});

builder.Services.AddScoped<DocumentSession>(sp =>
{
    var store = sp.GetRequiredService<DocumentStore>();
    return (DocumentSession) store.CreateSession();
});

builder.Services.AddScoped<ITodoListRepository, CosmodustTodoListRepository>();
builder.Services.AddScoped<IAccountRepository, CosmodustAccountRepository>();
builder.Services.AddScoped<ITodoItemRepository, CosmodustTodoItemRepository>();
builder.Services.AddScoped<IUnitOfWork, CosmodustUnitOfWork>();
builder.Services.AddSingleton<QueryFacade>(sp => new QueryFacade(
    sp.GetRequiredService<CosmosClient>(),
    databaseName: "reminderdb",
    sp.GetRequiredService<SqlParameterCache>()));
builder.Services.AddSingleton<SqlParameterCache>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
