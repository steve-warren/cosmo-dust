using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Cosmodust.Serialization;

/// <summary>
/// Provides a cache for storing and retrieving SQL parameter values for a given type. This class is thread-safe.
/// </summary>
public sealed class SqlParameterObjectTypeResolver
{
    private readonly ConcurrentDictionary<Type, ReadOnlyDictionary<string, Func<object, object?>>> _typeCache;

    public SqlParameterObjectTypeResolver(
        ConcurrentDictionary<Type, ReadOnlyDictionary<string, Func<object, object?>>>? typeCache = default)
    {
        _typeCache = typeCache ??
                     new ConcurrentDictionary<Type, ReadOnlyDictionary<string, Func<object, object?>>>();
    }

    /// <summary>
    /// Extracts the parameter values from the given object and returns them as a collection of name-value pairs.
    /// This method is thread-safe.
    /// </summary>
    /// <param name="parameters">The object whose parameter values need to be extracted.</param>
    /// <returns>A collection of name-value pairs representing the property values of the given object.</returns>
    public IEnumerable<(string Name, object? Value)> ExtractParametersFromObject(object? parameters)
    {
        if (parameters is null)
            yield break;

        var getters = GetOrAdd(parameters.GetType());

        foreach (var getter in getters)
            yield return (Name: getter.Key, Value: getter.Value(parameters));
    }

    private ReadOnlyDictionary<string, Func<object, object?>> GetOrAdd(Type type) =>
        _typeCache.GetOrAdd(
            key: type,
            value: BuildPropertyGetterLambdas(type));

    private static ReadOnlyDictionary<string, Func<object, object?>> BuildPropertyGetterLambdas(Type type)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead);

        var getters = new Dictionary<string, Func<object, object?>>();

        foreach (var property in properties)
        {
            var parameter = Expression.Parameter(typeof(object), "obj");
            var cast = Expression.Convert(parameter, type);
            var propertyAccess = Expression.Property(cast, property);
            var castResult = Expression.Convert(propertyAccess, typeof(object));

            var lambda = Expression.Lambda<Func<object, object?>>(castResult, parameter);
            var compiledLambda = lambda.Compile();

            getters.Add($"@{property.Name}", compiledLambda);
        }

        return getters.AsReadOnly();
    }
}
