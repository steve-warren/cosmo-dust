using System.Reflection;
using Cosmodust.Query;

namespace Cosmodust;

public class EntityConfiguration
{
    public EntityConfiguration(Type entityType)
    {
        EntityType = entityType;
    }

    public Type EntityType { get; }
    public string ContainerName { get; set; } = string.Empty;
    public IStringSelector IdSelector { get; set; } = NullStringSelector.Instance;
    public IStringSelector PartitionKeySelector { get; set; } = NullStringSelector.Instance;
    public HashSet<FieldAccessor> Fields { get; set; } = new();
    public HashSet<PropertyAccessor> Properties { get; set; } = new();
}
