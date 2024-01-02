using System.Linq.Expressions;
using System.Reflection;
using Cosmodust.Shared;

namespace Cosmodust.Serialization;

public record FieldAccessor(
    string FieldName,
    Type FieldType,
    Func<object, object?> Getter,
    Action<object, object?> Setter)
{
    public static FieldAccessor Create(string fieldName, Type type)
    {
        var fieldInfo = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic) 
                        ?? throw new InvalidOperationException(
                            $"The type '{type.Name}' does not have a field named '{fieldName}'.");

        return Create(fieldInfo);
    }

    private static FieldAccessor Create(FieldInfo fieldInfo)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");

        Ensure.NotNull(fieldInfo.DeclaringType);
        
        var instanceCast = fieldInfo.IsStatic
            ? null
            : Expression.Convert(instance, fieldInfo.DeclaringType);

        var valueCast = Expression.Convert(value, fieldInfo.FieldType);

        var getter = CompileGetterExpression(instance, fieldInfo, instanceCast);
        var setter = CompileSetterExpression(instance, instanceCast, fieldInfo, valueCast, value);

        return new FieldAccessor(fieldInfo.Name, fieldInfo.FieldType, getter, setter);
    }

private static Func<object, object?> CompileGetterExpression(
    ParameterExpression instance,
    FieldInfo fieldInfo,
    UnaryExpression? instanceCast)
    {
        var getterExpression = Expression.Convert(Expression.Field(instanceCast, fieldInfo), typeof(object));
        return Expression.Lambda<Func<object, object?>>(getterExpression, instance).Compile();
    }

private static Action<object, object?> CompileSetterExpression(
    ParameterExpression instance,
    UnaryExpression? instanceCast,
    FieldInfo fieldInfo,
    UnaryExpression valueCast,
    ParameterExpression value)
    {
        var setterExpression = Expression.Assign(Expression.Field(instanceCast, fieldInfo), valueCast);
        return Expression.Lambda<Action<object, object?>>(setterExpression, instance, value).Compile();
    }
}
