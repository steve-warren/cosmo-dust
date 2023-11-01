using System.Text.Json.Serialization.Metadata;
using Cosmodust.Shared;
using Cosmodust.Store;
using Cosmodust.Tracking;

namespace Cosmodust.Json;

public class DocumentETagJsonTypeModifier : IJsonTypeModifier
{
    private const string ETagPropertyName = "_etag";

    private readonly EntityConfigurationProvider _entityConfigurationProvider;
    private readonly JsonSerializerPropertyStore _jsonSerializerPropertyStore;

    public DocumentETagJsonTypeModifier(
        EntityConfigurationProvider entityConfigurationProvider,
        JsonSerializerPropertyStore jsonSerializerPropertyStore)
    {
        _entityConfigurationProvider = entityConfigurationProvider;
        _jsonSerializerPropertyStore = jsonSerializerPropertyStore;
    }

    public void Modify(JsonTypeInfo jsonTypeInfo)
    {
        if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        if (!_entityConfigurationProvider.HasEntityConfiguration(jsonTypeInfo.Type))
            return;

        var eTagJsonPropertyInfo = jsonTypeInfo.CreateJsonPropertyInfo(
            propertyType: typeof(string),
            name: ETagPropertyName);

        eTagJsonPropertyInfo.Set = WriteETagToPropertyStore;
        eTagJsonPropertyInfo.Get = ReadETagFromPropertyStore;

        jsonTypeInfo.Properties.Add(eTagJsonPropertyInfo);
    }

    private void WriteETagToPropertyStore(object entity, object? value)
    {
        _jsonSerializerPropertyStore.WritePropertyValue(entity, ETagPropertyName, value);
    }

    private object? ReadETagFromPropertyStore(object entity)
    {
        return _jsonSerializerPropertyStore.ReadPropertyValue(entity, ETagPropertyName);
    }
}