using Cosmodust.Shared;

namespace Cosmodust.Store;

public readonly record struct PolymorphicDerivedType
{
    /// <summary>
    /// Represents a polymorphic derived type that implements an interface or derives from a base type.
    /// </summary>
    /// <param name="baseOrInterfaceType">The base type or interface that the derived type should implement.</param>
    /// <param name="derivedType">The derived type that implements the base type or interface.</param>
    /// <param name="typeDiscriminator">An optional discriminator used to differentiate between different derived types.</param>
    public PolymorphicDerivedType(Type baseOrInterfaceType,
        Type derivedType,
        string? typeDiscriminator = null)
    {
        Ensure.NotNull(baseOrInterfaceType);
        Ensure.NotNull(derivedType);

        if (!baseOrInterfaceType.IsAssignableFrom(derivedType))
            throw new ArgumentException(
                $"Type '{derivedType.Name}' does not implement interface or derive from '{baseOrInterfaceType.Name}'.");

        BaseOrInterfaceType = baseOrInterfaceType;
        DerivedType = derivedType;
        TypeDiscriminator = string.IsNullOrEmpty(typeDiscriminator)
            ? derivedType.Name
            : typeDiscriminator;
    }

    public Type BaseOrInterfaceType { get; }
    public Type DerivedType { get; }
    public string TypeDiscriminator { get; }
}
