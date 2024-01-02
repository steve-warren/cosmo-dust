using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Cosmodust.Json;
using Cosmodust.Shared;
using Cosmodust.Store;
using Cosmodust.Tracking;

namespace Cosmodust.Extensions;

public class CosmodustJsonOptions
{
    private readonly HashSet<JsonConverter> _jsonConverters = [];
    private readonly Lazy<PolymorphicDerivedTypeModifier> _polymorphicDerivedTypeModifier = new();
    private readonly HashSet<IJsonTypeModifier> _jsonTypeModifiers = [];
    private readonly JsonNamingPolicy _jsonNamingPolicy = JsonNamingPolicy.CamelCase;

    public CosmodustJsonOptions(
        EntityConfigurationProvider entityConfigurationProvider,
        ShadowPropertyProvider shadowPropertyProvider)
    {
        WithJsonTypeModifier(new BackingFieldJsonTypeModifier(entityConfigurationProvider, _jsonNamingPolicy));
        WithJsonTypeModifier(new DocumentETagJsonTypeModifier(entityConfigurationProvider, shadowPropertyProvider));
        WithJsonTypeModifier(new IdJsonTypeModifier(entityConfigurationProvider, _jsonNamingPolicy));
        WithJsonTypeModifier(new PartitionKeyJsonTypeModifier(entityConfigurationProvider, _jsonNamingPolicy));
        WithJsonTypeModifier(new PropertyJsonTypeModifier(entityConfigurationProvider, _jsonNamingPolicy));
        WithJsonTypeModifier(new PropertyPrivateSetterJsonTypeModifier(entityConfigurationProvider));
        WithJsonTypeModifier(new ShadowPropertyJsonTypeModifier(entityConfigurationProvider));
        WithJsonTypeModifier(new TypeMetadataJsonTypeModifier(entityConfigurationProvider));
    }

    public bool SerializeEnumsToStrings { get; set; }

    public CosmodustJsonOptions SerializePrivateProperties()
    {
        return this;
    }

    public CosmodustJsonOptions SerializePrivateFields()
    {
        return this;
    }

    public CosmodustJsonOptions CamelCase()
    {
        return this;
    }

    public CosmodustJsonOptions SerializeEntityTypeInfo()
    {
        return this;
    }

    public CosmodustJsonOptions WithJsonTypeModifier(IJsonTypeModifier jsonTypeModifier)
    {
        _jsonTypeModifiers.Add(jsonTypeModifier);

        return this;
    }

    public CosmodustJsonOptions WithConverter(JsonConverter jsonConverter)
    {
        _jsonConverters.Add(jsonConverter);

        return this;
    }

    public CosmodustJsonOptions WithPolymorphicType<TInterfaceType, TDerivedType>() where TDerivedType : TInterfaceType
    {
        _polymorphicDerivedTypeModifier.Value.AddPolymorphicDerivedType(
            new PolymorphicDerivedType(baseOrInterfaceType: typeof(TInterfaceType),
                derivedType: typeof(TDerivedType)));

        return this;
    }

    public JsonSerializerOptions Build()
    {
        var jsonTypeInfoResolver = new DefaultJsonTypeInfoResolver();

        foreach (var action in _jsonTypeModifiers)
            jsonTypeInfoResolver.Modifiers.Add(action.Modify);

        if (_polymorphicDerivedTypeModifier.IsValueCreated)
            jsonTypeInfoResolver.Modifiers.Add(_polymorphicDerivedTypeModifier.Value.Modify);

        var options =  new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = _jsonNamingPolicy,
            TypeInfoResolver = jsonTypeInfoResolver
        };

        if (SerializeEnumsToStrings)
            options.Converters.Add(new JsonStringEnumConverter(_jsonNamingPolicy));

        foreach(var converter in _jsonConverters)
            options.Converters.Add(converter);

        return options;
    }
}
