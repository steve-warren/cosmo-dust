using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cosmodust.Cosmos.Tests.Domain.Accounts;
using Cosmodust.Cosmos.Tests.Domain.Blogs;
using Cosmodust.Extensions;
using Cosmodust.Json;
using Cosmodust.Linq;
using Cosmodust.Operations;
using Cosmodust.Query;
using Cosmodust.Serialization;
using Cosmodust.Store;
using Cosmodust.Tests.Domain.Accounts;
using Cosmodust.Tests.Domain.Todo;
using Cosmodust.Tests.Fixtures;
using Cosmodust.Tracking;
using KsuidDotNet;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cosmodust.Tests;

public class CosmosDatabaseTests : IClassFixture<CosmosTextFixture>
{
    private readonly DocumentStore _store;
    private readonly QueryFacade _queryFacade;

    public CosmosDatabaseTests(CosmosTextFixture configurationTextFixture)
    {
        var configuration = configurationTextFixture.Configuration;

        var sqlParameterCache = new SqlParameterObjectTypeResolver();
        var entityConfigurationProvider = new EntityConfigurationProvider();
        var jsonPropertyBroker = new ShadowPropertyProvider();
        var jsonOptions = new CosmodustJsonOptions(
            entityConfigurationProvider,
            jsonPropertyBroker);

        var modelBuilder = new ModelBuilder(
            jsonOptions,
            jsonPropertyBroker,
            entityConfigurationProvider);

        modelBuilder.DefineEntity<Account>()
            .WithId(e => e.Username)
            .WithPartitionKey(e => "account", "partitionKey")
            .WithDomainEvents(
                "_domainEvents",
                () => Ksuid.NewKsuid("ev_"))
            .ToContainer("accounts");

        modelBuilder.DefineEntity<AccountPlan>()
            .WithId(e => e.Id)
            .WithPartitionKey(
                e => e.Id,
                "ownerId")
            .ToContainer("accountPlans");

        modelBuilder.DefineEntity<BlogPost>()
            .WithId(e => e.Id)
            .WithPartitionKey(e => e.Id, "postId")
            .WithField("_likes")
            .ToContainer("blogPosts");

        modelBuilder.DefineEntity<BlogPostComment>()
            .WithId(e => e.Id)
            .WithShadowProperty<DateTime>("createdOn")
            .WithPartitionKey(e => e.PostId)
            .ToContainer("blogPosts");

        modelBuilder.DefineEntity<TodoItem>()
            .WithId(e => e.Id)
            .WithPartitionKey(e => e.Id, "partitionKey")
            .ToContainer("todo_poly");

        modelBuilder.DefinePolymorphicType<TodoItem.ICompletionState, TodoItem.IncompleteState>()
            .DefinePolymorphicType<TodoItem.ICompletionState, TodoItem.CompletedState>();

        modelBuilder.Build();

        var cosmodustOptions = new CosmodustOptions(
            jsonOptions,
            modelBuilder).WithDatabase("reminderdb");

        var serializer = new CosmodustJsonSerializer(jsonOptions);

        var cosmosClient = new CosmosClient(configuration["COSMOSDB_CONNECTIONSTRING"], new CosmosClientOptions()
        {
            Serializer = serializer
        });

        _store = new DocumentStore(
            new CosmosDatabase(cosmosClient, cosmodustOptions),
            entityConfigurationProvider,
            sqlParameterCache,
            jsonPropertyBroker);

        var nullLoggerFactory = new NullLoggerFactory();

        _queryFacade = new QueryFacade(
            client: cosmosClient,
            databaseName: "reminderdb",
            sqlParameterObjectTypeResolver: new SqlParameterObjectTypeResolver(),
            new Logger<QueryFacade>(nullLoggerFactory),
            new CosmodustQueryOptions
            {
                ExcludeCosmosMetadata = false,
                IncludeETag = true,
                RenameDocumentCollectionProperties = false
            });
    }

    [Fact]
    public async Task Can_Add_And_Find_Entity_In_Separate_Sessions()
    {
        var entity = new AccountPlan(Guid.NewGuid().ToString());

        var writeSession = _store.CreateSession();

        writeSession.Store(entity);
        await writeSession.CommitAsync();

        var readSession = _store.CreateSession();
        var readEntity = await readSession.FindAsync<AccountPlan>(entity.Id, entity.Id);

        readEntity.Should().BeEquivalentTo(entity, because: "we should be able to find the entity we just created");
    }

    [Fact]
    public async Task Can_Add_Find_Delete_Entity_In_Separate_Sessions()
    {
        var entity = new AccountPlan(Guid.NewGuid().ToString());

        var writeSession = _store.CreateSession();

        writeSession.Store(entity);
        await writeSession.CommitAsync();

        var deleteSession = _store.CreateSession();
        var deleteEntity = await deleteSession.FindAsync<AccountPlan>(entity.Id, entity.Id);

        deleteEntity.Should().NotBeNull(because: "we should be able to find the entity we just created");
        deleteEntity.Should().BeEquivalentTo(entity, because: "we should be able to find the entity we just created");

        deleteSession.Remove(deleteEntity!);
        await deleteSession.CommitAsync();

        var readSession = _store.CreateSession();
        var readEntity = await readSession.FindAsync<AccountPlan>(entity.Id, entity.Id);

        readEntity.Should().BeNull(because: "we should not be able to find the entity we just deleted");
    }

    [Fact]
    public async Task Can_Add_Find_Update_Entity_In_Separate_Session()
    {
        var entity = new AccountPlan(Guid.NewGuid().ToString());

        var writeSession = _store.CreateSession();

        writeSession.Store(entity);
        await writeSession.CommitAsync();

        var updateSession = _store.CreateSession();
        var updatedEntity = await updateSession.FindAsync<AccountPlan>(entity.Id, entity.Id);

        updatedEntity.Should().NotBeSameAs(entity, because: "the entity should be a different instance since it is from a different session");
        updatedEntity.Should().NotBeNull(because: "we should be able to find the entity we just created");

        updatedEntity!.Name = "Updated Plan";
        updateSession.Update(updatedEntity);

        await updateSession.CommitAsync();

        var readSession = _store.CreateSession();
        var readEntity = await readSession.FindAsync<AccountPlan>(entity.Id, entity.Id);

        readEntity.Should().NotBeSameAs(updatedEntity, because: "the entity should be a different instance since it is from a different session");
        readEntity.Should().NotBeNull(because: "we should be able to find the entity we just updated");
        readEntity!.Name.Should().Be(updatedEntity.Name, because: "we should have updated the entity name");
    }
    
    [Fact]
    public async Task Can_Update_And_Attach_Existing_Entity_In_Separate_Session_Without_ETag_Value()
    {
        var entityId = Guid.NewGuid().ToString();

        // create new entity
        var entity = new AccountPlan(id: entityId);
        var writeSession = _store.CreateSession();

        writeSession.Store(entity);
        await writeSession.CommitAsync();

        // attach entity to separate session
        var updateSession = _store.CreateSession();
        var attachEntity = new AccountPlan(id: entityId)
        {
            Name = "Updated Plan"
        };

        updateSession.Attach(attachEntity);
        
        var entry = updateSession.Entity(attachEntity);

        entry.Entity.Should().BeSameAs(attachEntity, because: "the entity should be the instance we just attached.");
        entry.Id.Should().Be(entityId, because: "the id should be set.");
        entry.State.Should().Be(EntityState.Unchanged, because: "an attached entity's state should be 'unchanged'.");

        // mark the entity for update
        updateSession.Update(attachEntity);
        
        await updateSession.CommitAsync();

        var readSession = _store.CreateSession();
        var readEntity = await readSession.FindAsync<AccountPlan>(entity.Id, entity.Id);

        readEntity.Should().NotBeSameAs(attachEntity, because: "the entity should be a different instance since it is from a different session");
        readEntity.Should().NotBeNull(because: "we should be able to find the entity we just updated");
        readEntity!.Name.Should().Be(attachEntity.Name, because: "we should have updated the entity name");
    }

    [Fact]
    public async Task Can_Update_Entity_With_Same_ETag_Values()
    {
        var entityId = Guid.NewGuid().ToString();

        // create new entity
        var entity = new AccountPlan(id: entityId);

        var writeSession = _store.CreateSession();
        writeSession.Store(entity);
        await writeSession.CommitAsync();

        var writeSessionEntry = writeSession.Entity(entity);

        // attach entity to separate session with etag
        var attachSession = _store.CreateSession();
        attachSession.Attach(
            entity,
            writeSessionEntry.ETag);
        
        var attachSessionEntry = attachSession.Entity(entity);

        attachSessionEntry.ETag.Should().Be(writeSessionEntry.ETag, because: "no modifications to the entity were made therefore the eTags should match.");

        attachSession.Update(entity);

        await attachSession.CommitAsync();
        
        attachSessionEntry.ETag.Should().NotBe(writeSessionEntry.ETag, because: "modifications were made therefore a new eTag should exist.");
    }
    
    [Fact]
    public async Task Prevent_Entity_Update_With_Different_ETag_Values_Outside_Transaction()
    {
        var entityId = Guid.NewGuid().ToString();

        // create new entity
        var entity = new AccountPlan(id: entityId);

        var writeSession = _store.CreateSession();
        writeSession.Store(entity);
        await writeSession.CommitAsync();

        // attach entity to separate session with etag
        var attachSession = _store.CreateSession();
        attachSession.Attach(
            entity,
            eTag: "000");
        
        attachSession.Update(entity);

        var result = await attachSession.CommitAsync();

        result.Results[0].Should()
            .BeOfType<ConcurrencyConflictDocumentOperationResult>(because: "concurrency conflict should be detected.");
    }

    [Fact]
    public async Task Can_Execute_Linq_Query_As_List()
    {
        var postId = Guid.NewGuid().ToString();
        var post = new BlogPost { Id = postId };

        var comments = new[]
        {
            new BlogPostComment { PostId = postId, Id = Guid.NewGuid().ToString(), Content = "Comment 1" },
            new BlogPostComment { PostId = postId, Id = Guid.NewGuid().ToString(), Content = "Comment 2" }
        };
        
        var writeSession = _store.CreateSession();

        writeSession.Store(post);
        writeSession.Store(comments[0]);
        writeSession.Store(comments[1]);
        
        await writeSession.CommitTransactionAsync();

        var readSession = _store.CreateSession();
        var readEntities = await readSession.Query<BlogPostComment>(partitionKey: postId)
            .Where(comment => comment.PostId == postId)
            .ToListAsync();

        readEntities.Should().HaveCount(2, because: "we should have found the two entities we just created");
        readEntities.Should().BeEquivalentTo(comments, because: "we should be able to query the entities we just created");
    }

    [Fact]
    public async Task Can_Execute_Linq_Query_With_Where_Clause_As_AsyncEnumerable()
    {
        var postId = Guid.NewGuid().ToString();
        var post = new BlogPost { Id = postId };

        var comments = new[]
        {
            new BlogPostComment { PostId = postId, Id = Guid.NewGuid().ToString(), Content = "Comment 1" },
            new BlogPostComment { PostId = postId, Id = Guid.NewGuid().ToString(), Content = "Comment 2" }
        };

        var writeSession = _store.CreateSession();

        writeSession.Store(post);
        writeSession.Store(comments[0]);
        writeSession.Store(comments[1]);

        await writeSession.CommitTransactionAsync();

        var readSession = _store.CreateSession();
        var readEntities = readSession.Query<BlogPostComment>(postId)
                           .Where(c => c.PostId == postId)
                           .ToAsyncEnumerable();

        var list = new List<BlogPostComment>();

        await foreach (var entity in readEntities)
            list.Add(entity);

        list.Should().HaveCount(2, because: "we should have found the two entities we just created");
        list.Should().BeEquivalentTo(comments, because: "we should be able to query the entities we just created");
    }

    [Fact]
    public async Task Can_Execute_Linq_Query_As_AsyncEnumerable()
    {
        var postId = Guid.NewGuid().ToString();
        var post = new BlogPost { Id = postId };

        var comments = new[]
        {
            new BlogPostComment { PostId = postId, Id = Guid.NewGuid().ToString(), Content = "Comment 1" },
            new BlogPostComment { PostId = postId, Id = Guid.NewGuid().ToString(), Content = "Comment 2" }
        };

        var writeSession = _store.CreateSession();

        writeSession.Store(post);
        writeSession.Store(comments[0]);
        writeSession.Store(comments[1]);

        await writeSession.CommitTransactionAsync();

        var readSession = _store.CreateSession();
        var readEntities = readSession.Query<BlogPostComment>(postId)
            .ToAsyncEnumerable();

        var list = new List<BlogPostComment>();

        await foreach (var entity in readEntities)
            list.Add(entity);

        list.Should().HaveCount(2, because: "we should have found the two entities we just created");
        list.Should().BeEquivalentTo(comments, because: "we should be able to query the entities we just created");
    }

    [Fact]
    public async Task Can_Execute_Linq_Query_As_FirstOrDefault()
    {
        var postId = Guid.NewGuid().ToString();
        var post = new BlogPost { Id = postId };

        var comments = new[]
        {
            new BlogPostComment { PostId = postId, Id = Guid.NewGuid().ToString(), Content = "Comment 1" },
            new BlogPostComment { PostId = postId, Id = Guid.NewGuid().ToString(), Content = "Comment 2" }
        };

        var writeSession = _store.CreateSession();

        writeSession.Store(post);
        writeSession.Store(comments[0]);
        writeSession.Store(comments[1]);

        await writeSession.CommitTransactionAsync();

        var readSession = _store.CreateSession();
        var readEntity = await readSession.Query<BlogPostComment>(postId)
            .Where(c => c.PostId == postId)
            .FirstOrDefaultAsync();

        readEntity.Should().NotBeNull(because: "we should have found the entity we just created");
        readEntity.Should().BeEquivalentTo(comments[0], because: "we should be able to query the entities we just created");
    }

    [Fact]
    public async Task Transactional_Batch()
    {
        var session = _store.CreateSession();

        var postId = Guid.NewGuid().ToString();
        var post = new BlogPost { Id = postId };
        var comment = new BlogPostComment { Id = Guid.NewGuid().ToString(), PostId = post.Id };

        session.Store(post);
        session.Store(comment);

        await session.CommitTransactionAsync();
    }

    [Fact]
    public async Task Backing_Field()
    {
        var writeSession = _store.CreateSession();

        var postId = DateTime.Now.ToFileTime().ToString();
        var post = new BlogPost { Id = postId };

        post.Like();
        post.GetLikes().Should().Be(1, because: "the post should have one like");
        post.PublishOn(DateTimeOffset.Now);

        writeSession.Store(post);

        await writeSession.CommitAsync();

        var readSession = _store.CreateSession();

        var readPost = await readSession.FindAsync<BlogPost>(postId, postId);

        readPost.Should().NotBeNull(because: "we should be able to find the post we just created");
        readPost!.GetLikes().Should().Be(1, because: "the post should have one like");
    }

    [Fact]
    public async Task Can_Query_Using_Raw_Sql_With_Parameters()
    {
        var writeSession = _store.CreateSession();

        var postId = Guid.NewGuid().ToString();
        var blogPost = new BlogPost
        {
            Id = postId,
            Title = "🦖Rawr SQL!",
            PublishedOn = new DateTimeOffset(new DateTime(2000, 1, 1), TimeSpan.FromHours(-5))
        };

        writeSession.Store(blogPost);
        await writeSession.CommitAsync();

        var readSession = _store.CreateSession();

        var query = readSession.Query<BlogPost>(
            partitionKey: postId,
            sql: "select * from c where c.id = @id",
            parameters: new { id = postId });

        var result = await query.FirstOrDefaultAsync();

        result.Should().BeEquivalentTo(expectation: blogPost, because: "the query should return an object by its id.");
    }

    [Fact]
    public async Task Query_Facade_Returns_Json_Document()
    {
        var pipe = new Pipe();

        await _queryFacade.ExecuteQueryAsync(
            pipeWriter: pipe.Writer,
            containerName: "todo",
            partitionKey: "a_2X011ldw0dogcauAbw0oExAv21H",
            sql: "select top 1 * from c where c.ownerId = @ownerId",
            parameters: new { ownerId = "a_2X011ldw0dogcauAbw0oExAv21H" });

        await pipe.Writer.CompleteAsync();
        
        var readResult = await pipe.Reader.ReadAsync();

        var document = JsonDocument.Parse(readResult.Buffer);

        document.RootElement
            .GetProperty("Documents")
            .EnumerateArray()
            .First()
            .GetProperty("ownerId")
            .GetString()
            .Should().Be("a_2X011ldw0dogcauAbw0oExAv21H");
    }

    [Fact]
    public async Task Can_Write_And_Read_Shadow_Property()
    {
        var id = Guid.NewGuid().ToString();

        var writeSession = _store.CreateSession();

        var comment = new BlogPostComment
        {
            Id = id,
            PostId = Guid.NewGuid().ToString()
        };

        var shadowProperty = new { Name = "createdOn", Value = new DateTime(year: 2002, month: 12, day: 31) };

        writeSession.Store(comment);

        writeSession.Entity(comment)
               !.WriteShadowProperty(shadowProperty.Name, shadowProperty.Value);

        await writeSession.CommitAsync();

        var readSession = _store.CreateSession();

        var readComment = await readSession.FindAsync<BlogPostComment>(
            id: id,
            partitionKey: comment.PostId);

        readComment.Should().NotBeNull();

        var readProperty = readSession.Entity(readComment!)!.ReadShadowProperty<DateTime>(shadowProperty.Name);

        readProperty.Should().Be(shadowProperty.Value);
    }

    [Fact]
    public async Task Should_Return_Entity_ETag_During_Read_Operation()
    {
        var id = Guid.NewGuid().ToString();

        var writeSession = _store.CreateSession();

        var comment = new BlogPostComment
        {
            Id = id,
            PostId = Guid.NewGuid().ToString()
        };

        writeSession.Store(comment);
        await writeSession.CommitAsync();

        var readSession = _store.CreateSession();

        var readComment = await readSession.FindAsync<BlogPostComment>(
            id: id,
            partitionKey: comment.PostId);

        Assert.NotNull(readComment);

        readComment.Should().NotBeNull(because: "the entity should be returned from the database.");
        var entry = readSession.Entity(readComment);

        entry.ETag.Should().NotBeEmpty(because: "the ETag should be returned from the database.");
    }

    [Fact]
    public async Task Should_Return_Entity_ETag_During_Linq_Query_Operation()
    {
        var postId = Guid.NewGuid().ToString();

        var writeSession = _store.CreateSession();

        for (var i = 0; i < 2; i++)
        {
            var comment = new BlogPostComment { Id = Guid.NewGuid().ToString(), PostId = postId };

            writeSession.Store(comment);
        }

        await writeSession.CommitAsync();

        var readSession = _store.CreateSession();

        var readComments = await readSession.Query<BlogPostComment>(
            partitionKey: postId)
            .Where(c => c.PostId == postId)
            .ToListAsync();

        foreach (var readComment in readComments)
        {
            Assert.NotNull(readComment);

            var entry = readSession.Entity(readComment);

            entry.ETag.Should().NotBeEmpty(because: "the ETag should be returned from the database.");
        }
    }

    [Fact]
    public async Task Should_Return_Entity_ETag_During_Sql_Query_Operation()
    {
        var postId = Guid.NewGuid().ToString();

        var writeSession = _store.CreateSession();

        for (var i = 0; i < 2; i++)
        {
            var comment = new BlogPostComment { Id = Guid.NewGuid().ToString(), PostId = postId };

            writeSession.Store(comment);
        }

        await writeSession.CommitAsync();

        var readSession = _store.CreateSession();

        var readComments = await readSession.Query<BlogPostComment>(
            partitionKey: postId,
            sql: "select * from c where c.postId = @postId",
            parameters: new { postId = postId }).ToListAsync();

        foreach (var readComment in readComments)
        {
            Assert.NotNull(readComment);

            var entry = readSession.Entity(readComment);

            entry.ETag.Should().NotBeEmpty(because: "the ETag should be returned from the database.");
        }
    }

    [Fact]
    [Trait("gh_feature", "100")]
    public async Task Can_Serialize_Interface()
    {
        var writeSession = _store.CreateSession();

        var todo = new TodoItem(Guid.NewGuid().ToString());

        writeSession.Store(todo);

        await writeSession.CommitAsync();

        var updateSession = _store.CreateSession();

        var updateItem = await updateSession.FindAsync<TodoItem>(todo.Id, todo.Id);

        Ensure.NotNull(updateItem);
        
        var incomplete = (TodoItem.IncompleteState) updateItem.CompletionState;
        var completed = incomplete.Complete(DateTimeOffset.Now);
        
        updateItem.ChangeCompletionState(completed);
        
        updateSession.Update(updateItem);

        await updateSession.CommitAsync();

        var readSession = _store.CreateSession();

        var readItem = await readSession.FindAsync<TodoItem>(todo.Id, todo.Id);

        Ensure.NotNull(readItem);
        
        readItem.CompletionState.Should()
            .BeOfType<TodoItem.CompletedState>(because: "the item should be in the completed state.");
    }

    [Fact]
    [Trait("gh_feature", "104")]
    public async Task Can_Have_Entity_With_Id_Mapped_To_Different_Property()
    {
        var writeSession = _store.CreateSession();
        
        var account = new Account { Email = "michael_scott@contoso", Username = "mg_scott_" + Guid.NewGuid() };
        
        writeSession.Store(account);

        await writeSession.CommitAsync();

        var readSession = _store.CreateSession();

        var readAccount = await readSession.FindAsync<Account>(
            id: account.Username,
            partitionKey: "account");

        readAccount.Should().NotBeNull();
        
        Ensure.NotNull(readAccount);
    }

    [Fact]
    public async Task Can_Raise_Domain_Events()
    {
        var writeSession = _store.CreateSession();

        var account = new Account { Email = "michael_scott@contoso", Username = "mg_scott_" + Guid.NewGuid() };

        var newAccountEvent = new { Name = "AccountCreated", account.Email, account.Username };

        account.RaiseEvent(newAccountEvent);

        writeSession.Store(account);

        await writeSession.CommitTransactionAsync();
    }
}
