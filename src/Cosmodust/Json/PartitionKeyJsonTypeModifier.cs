using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cosmodust.Store;

namespace Cosmodust.Json;

public sealed class PartitionKeyJsonTypeModifier : IJsonTypeModifier
{
    private readonly EntityConfigurationProvider _entityConfigurations;
    private readonly JsonNamingPolicy _jsonNamingPolicy;

    public PartitionKeyJsonTypeModifier(
        EntityConfigurationProvider entityConfigurations,
        JsonNamingPolicy jsonNamingPolicy)
    {
        _entityConfigurations = entityConfigurations;
        _jsonNamingPolicy = jsonNamingPolicy;
    }

    public void Modify(JsonTypeInfo jsonTypeInfo)
    {
        if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        if (!_entityConfigurations.TryGetEntityConfiguration(jsonTypeInfo.Type, out var entityConfiguration))
            return;

        if (entityConfiguration.IsPartitionKeyDefinedInEntity)
            return;

        var partitionKeyName = _jsonNamingPolicy.ConvertName(
            entityConfiguration.PartitionKeyName);

        var jsonPropertyInfo = jsonTypeInfo.CreateJsonPropertyInfo(
            propertyType: typeof(string),
            name: partitionKeyName);

        jsonPropertyInfo.Get = entityConfiguration.PartitionKeySelector.GetString;

        jsonTypeInfo.Properties.Add(jsonPropertyInfo);
    }
}
