using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using CommunityToolkit.HighPerformance.Buffers;
using Cosmodust.Extensions;
using Cosmodust.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Cosmodust;

public class QueryFacade
{
    private readonly JsonReaderOptions _readerOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly Dictionary<string, string> _propertyRename = new();
    private readonly JsonWriterOptions _jsonWriterOptions;
    private readonly Database _database;
    private readonly SqlParameterObjectTypeResolver _sqlParameterObjectTypeResolver;
    private readonly ILogger<QueryFacade> _logger;
    private readonly Dictionary<string, JsonModifierType> _jsonModifiers = new();

    public QueryFacade(
        CosmodustOptions options,
        CosmosClient client,
        SqlParameterObjectTypeResolver sqlParameterObjectTypeResolver,
        ILogger<QueryFacade> logger)
        : this(
            client,
            options.DatabaseId,
            sqlParameterObjectTypeResolver,
            logger,
            options.QueryOptions)
    {

    }

    public QueryFacade(
        CosmosClient client,
        string databaseName,
        SqlParameterObjectTypeResolver sqlParameterObjectTypeResolver,
        ILogger<QueryFacade> logger,
        CosmodustQueryOptions? queryOptions = null)
    {
        _database = client.GetDatabase(databaseName);
        _sqlParameterObjectTypeResolver = sqlParameterObjectTypeResolver;
        _logger = logger;
        
        var options = queryOptions ?? new CosmodustQueryOptions();

        if (options.RenameDocumentCollectionProperties)
        {
            _jsonModifiers.Add("Documents", JsonModifierType.RenameProperty);
            _jsonModifiers.Add("_count", JsonModifierType.RenameProperty);
            _propertyRename.Add("Documents", options.DocumentCollectionPropertyName);
            _propertyRename.Add("_count", options.DocumentCollectionCountPropertyName);
        }

        if (options.ExcludeCosmosMetadata)
        {
            _jsonModifiers.Add("_rid", JsonModifierType.SkipProperty);
            _jsonModifiers.Add("_self", JsonModifierType.SkipProperty);
            _jsonModifiers.Add("_attachments", JsonModifierType.SkipProperty);
            _jsonModifiers.Add("_ts", JsonModifierType.SkipProperty);
        }

        _jsonModifiers.Add("_etag", options.IncludeETag 
            ? JsonModifierType.EscapeStringValue 
            : JsonModifierType.SkipProperty);

        _jsonWriterOptions = new JsonWriterOptions
        {
            Indented = options.IndentJsonOutput,
            SkipValidation = true
        };
    }

    /// <summary>
    /// Executes a Cosmos DB query asynchronously without a partition key and writes the result to the specified <paramref name="pipeWriter"/>.
    /// </summary>
    /// <param name="pipeWriter">The <see cref="PipeWriter"/> to write the query result to.</param>
    /// <param name="containerName">The name of the container in which the query should be executed.</param>
    /// <param name="sql">The SQL query string to execute.</param>
    /// <param name="parameters">Additional parameters to pass to the SQL query (optional).</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous query execution operation.</returns>
    public ValueTask ExecuteQueryAsync(
        PipeWriter pipeWriter,
        string containerName,
        string sql,
        object? parameters = default) =>
        ExecuteQueryAsync(
            pipeWriter,
            containerName,
            sql,
            PartitionKey.None,
            parameters);

    /// <summary>
    /// Executes a SQL query asynchronously and writes the result to the given <paramref name="pipeWriter"/>.
    /// </summary>
    /// <param name="pipeWriter">The <see cref="PipeWriter"/> to write the result to.</param>
    /// <param name="containerName">The name of the container.</param>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="partitionKey">The partition key value. Can be null.</param>
    /// <param name="parameters">Optional parameters for the query.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    public ValueTask ExecuteQueryAsync(
        PipeWriter pipeWriter,
        string containerName,
        string sql,
        string? partitionKey,
        object? parameters = default) =>
        ExecuteQueryAsync(
            pipeWriter,
            containerName,
            sql,
            new PartitionKey(partitionKey),
            parameters);

    private async ValueTask ExecuteQueryAsync(
        PipeWriter pipeWriter,
        string containerName,
        string sql,
        PartitionKey? partitionKey,
        object? parameters = default)
    {
        var container = _database.GetContainer(containerName);
        var query = BuildQueryDefinition(sql, parameters);

        var requestOptions = new QueryRequestOptions
        {
            PartitionKey = partitionKey,
            MaxConcurrency = -1,
            MaxItemCount = -1,
            MaxBufferedItemCount = -1
        };

        using var feed = container.GetItemQueryStreamIterator(query, requestOptions: requestOptions);

        await using var writer = new Utf8JsonWriter(pipeWriter, _jsonWriterOptions);

        await WriteJsonAsync(feed, writer).ConfigureAwait(false);
    }

    private async Task WriteJsonAsync(
        FeedIterator feed,
        Utf8JsonWriter writer)
    {
        Task<ResponseMessage> readNextTask = feed.ReadNextAsync();
        var flushTask = Task.CompletedTask;

        while (true)
        {
            var completedTask = await Task.WhenAny(readNextTask, flushTask).ConfigureAwait(false);

            if (completedTask == readNextTask)
            {
                // ensure previous flush operation is completed before TransformJson
                await flushTask.ConfigureAwait(false);

                using var response = await readNextTask.ConfigureAwait(false);
                using var stream = response.Content as MemoryStream;

                Debug.Assert(stream != null);

                // transform json and start flushing
                TransformJson(_jsonModifiers, _propertyRename, stream, _readerOptions, writer);
                flushTask = writer.FlushAsync();

                // start next read operation
                if (feed.HasMoreResults)
                    readNextTask = feed.ReadNextAsync();

                else
                    break;
            }

            // complete FlushAsync task
            else if (completedTask == flushTask)
                await flushTask.ConfigureAwait(false);
        }

        // ensure the last flush operation is completed
        await flushTask.ConfigureAwait(false);
    }

    private QueryDefinition BuildQueryDefinition(string sql, object? parameters)
    {
        var query = new QueryDefinition(sql);

        foreach (var parameter in _sqlParameterObjectTypeResolver.ExtractParametersFromObject(parameters))
            query.WithParameter(parameter.Name, parameter.Value);

        return query;
    }

    private static void TransformJson(
        Dictionary<string, JsonModifierType> jsonModifiers,
        Dictionary<string, string> propertyRename,
        MemoryStream stream,
        JsonReaderOptions jsonReaderOptions,
        Utf8JsonWriter writer)
    {
        var jsonData = new ReadOnlySpan<byte>(stream.GetBuffer(), 0, (int) stream.Length);
        var reader = new Utf8JsonReader(jsonData, jsonReaderOptions);

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    var propertyName = StringPool.Shared.GetOrAdd(reader.ValueSpan, Encoding.UTF8);

                    if (!jsonModifiers.TryGetValue(propertyName, out var jsonModifier))
                        writer.WritePropertyName(reader.ValueSpan);

                    else switch (jsonModifier)
                    {
                        case JsonModifierType.SkipProperty:
                            reader.Skip();
                            continue;
                        case JsonModifierType.EscapeStringValue:
                            {
                                var etagValue = reader.GetString(); // todo perf - avoid string alloc?
                                writer.WritePropertyName(propertyName);
                                writer.WriteStringValue(etagValue);
                                continue;
                            }
                        case JsonModifierType.RenameProperty:
                            writer.WritePropertyName(propertyRename[propertyName]);
                            continue;
                        default:
                            throw new JsonException("Unable to modify property.");
                    }

                    continue;

                case JsonTokenType.StartObject:
                    writer.WriteStartObject();
                    continue;

                case JsonTokenType.EndObject:
                    writer.WriteEndObject();
                    continue;

                case JsonTokenType.StartArray:
                    writer.WriteStartArray();
                    continue;

                case JsonTokenType.EndArray:
                    writer.WriteEndArray();
                    continue;

                case JsonTokenType.String:
                    writer.WriteStringValue(reader.ValueSpan);
                    continue;
                case JsonTokenType.Null:
                    writer.WriteNullValue();
                    continue;
                case JsonTokenType.None:
                case JsonTokenType.Comment:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                default:
                    writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                    continue;
            }
        }
    }
}

internal enum JsonModifierType
{
    None,
    SkipProperty,
    EscapeStringValue,
    RenameProperty
}
