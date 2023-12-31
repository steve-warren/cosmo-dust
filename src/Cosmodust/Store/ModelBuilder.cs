using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Cosmodust.Extensions;
using Cosmodust.Json;
using Cosmodust.Session;
using Cosmodust.Shared;
using Cosmodust.Tracking;

namespace Cosmodust.Store;

/// <summary>
/// A class that allows building entity configurations for a data model.
/// </summary>
public class ModelBuilder
{
    private readonly CosmodustJsonOptions _jsonOptions;
    private readonly ShadowPropertyProvider _shadowPropertyProvider;
    private readonly EntityConfigurationProvider _entityConfigurationProvider;
    private readonly List<IEntityBuilder> _entityBuilders = new();

    public ModelBuilder(
        CosmodustJsonOptions jsonOptions,
        ShadowPropertyProvider shadowPropertyProvider,
        EntityConfigurationProvider entityConfigurationProvider)
    {
        Ensure.NotNull(jsonOptions);
        Ensure.NotNull(shadowPropertyProvider);

        _jsonOptions = jsonOptions;
        _shadowPropertyProvider = shadowPropertyProvider;
        _entityConfigurationProvider = entityConfigurationProvider;
    }

    /// <summary>
    /// Returns an instance of <see cref="EntityBuilder{TEntity}"/> for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to be built.</typeparam>
    /// <returns>An instance of <see cref="EntityBuilder{TEntity}"/>.</returns>
    public EntityBuilder<TEntity> DefineEntity<TEntity>() where TEntity : class
    {
        var entityBuilder = new EntityBuilder<TEntity>(_shadowPropertyProvider);

        _entityBuilders.Add(entityBuilder);

        return entityBuilder;
    }

    /// <summary>
    /// Configures a type to be converted to and from its database representation.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <typeparam name="TConversion">The type to convert to and from.</typeparam>
    /// <param name="fromEntity">An expression representing the conversion from the entity type to the conversion type.</param>
    /// <param name="toEntity">An expression representing the conversion from the conversion type to the entity type.</param>
    /// <returns>The same <see cref="ModelBuilder"/> instance so that multiple calls can be chained.</returns>
    public ModelBuilder HasConversion<TEntity, TConversion>(
        Expression<Func<TEntity, TConversion>> fromEntity,
        Expression<Func<TConversion, TEntity>> toEntity)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Configures the model to use a JSON converter for the specified value object type.
    /// </summary>
    /// <typeparam name="TEnumeration">The type of the value object.</typeparam>
    /// <returns>The current instance of <see cref="ModelBuilder"/>.</returns>
    public ModelBuilder DefineEnumeration<TEnumeration>() where TEnumeration : class
    {
        var jsonConverter = new ValueObjectJsonConverter<TEnumeration>();

        _jsonOptions.WithConverter(jsonConverter);

        return this;
    }

    /// <summary>
    /// Defines a polymorphic type for the specified interface and derived types.
    /// </summary>
    /// <typeparam name="TInterfaceType">The interface type.</typeparam>
    /// <typeparam name="TDerivedType">The derived type.</typeparam>
    /// <returns>The current instance of the <see cref="ModelBuilder"/> class.</returns>
    public ModelBuilder DefinePolymorphicType<TInterfaceType, TDerivedType>() where TDerivedType : TInterfaceType
    {
        _jsonOptions.WithPolymorphicType<TInterfaceType, TDerivedType>();

        return this;
    }

    /// <summary>
    /// Configures a mapping between an enum type and its underlying value type.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <typeparam name="TConversion">The underlying value type.</typeparam>
    /// <param name="fromEnum">An expression that specifies how to convert from the enum type to the underlying value type.</param>
    /// <param name="toEnum">An expression that specifies how to convert from the underlying value type to the enum type.</param>
    /// <returns>The same <see cref="ModelBuilder"/> instance so that multiple calls can be chained.</returns>
    public ModelBuilder HasEnum<TEnum, TConversion>(
        Expression<Func<TEnum, TConversion>> fromEnum,
        Expression<Func<TConversion, TEnum>> toEnum) where TEnum : Enum
    {
        throw new NotImplementedException();
    }

    public void Build()
    {
        foreach (var builder in _entityBuilders)
        {
            var config = builder.Build();
            _entityConfigurationProvider.AddEntityConfiguration(config);
        }

        _entityConfigurationProvider.Build();
    }
}
