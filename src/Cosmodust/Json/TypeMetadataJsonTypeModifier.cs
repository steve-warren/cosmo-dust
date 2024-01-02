using System.Text.Json.Serialization.Metadata;
using Cosmodust.Store;

namespace Cosmodust.Json;

public sealed class TypeMetadataJsonTypeModifier : IJsonTypeModifier
{
    private readonly EntityConfigurationProvider _entityConfigurationProvider;

    public TypeMetadataJsonTypeModifier(EntityConfigurationProvider entityConfigurationProvider)
    {
        _entityConfigurationProvider = entityConfigurationProvider;
    }
    
    public void Modify(JsonTypeInfo jsonTypeInfo)
    {
        if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        if (!_entityConfigurationProvider.HasEntityConfiguration(jsonTypeInfo.Type))
            return;

        var jsonPropertyInfo = jsonTypeInfo.CreateJsonPropertyInfo(typeof(string), "_type");
        jsonPropertyInfo.Get = GetTypeName;

        jsonTypeInfo.Properties.Add(jsonPropertyInfo);
    }

    private static object GetTypeName(object obj) => obj.GetType().Name;
}
