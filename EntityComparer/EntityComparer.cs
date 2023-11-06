using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace EntityComparer;

public interface IMappingExpression
{
    void Map(object source, object destination, object diff, EntityComparer configuration);
}

public class EntityComparer
{
    private readonly Dictionary<string, IMappingExpression> _mappings = new();

    public void CreateMap<TSource, TDestination, TDiff>(
        Action<MappingExpression<TSource, TDestination, TDiff>>? mappingConfig = null)
    {
        var key = GetMappingKey(typeof(TSource), typeof(TDestination), typeof(TDiff));
        var mappingExpression = new MappingExpression<TSource, TDestination, TDiff>();

        var sourceProperties = typeof(TSource).GetProperties();
        var destinationProperties = typeof(TDestination).GetProperties();
        var diffProperties = typeof(TDiff).GetProperties();

        foreach (var diffProp in diffProperties)
        {
            var sourceProp = sourceProperties.FirstOrDefault(p => p.Name == diffProp.Name);
            var destinationProp = destinationProperties.FirstOrDefault(p => p.Name == diffProp.Name);
            if (destinationProp != null && sourceProp != null)
            {
                var accessor = CreatePropertyAccessor<TDiff>(diffProp.Name);

                mappingExpression.ForMember(accessor, src => sourceProp.GetValue(src),
                    src => destinationProp.GetValue(src));
            }
        }

        mappingConfig?.Invoke(mappingExpression);

        _mappings[key] = mappingExpression;
    }
    
    public TDiff Map<TSource, TDestination, TDiff>(TSource source, TDestination destination)
    {
        var key = GetMappingKey(typeof(TSource), typeof(TDestination), typeof(TDiff));
        if (_mappings.TryGetValue(key, out var expression))
        {
            var diff = Activator.CreateInstance<TDiff>();
            expression.Map(source, destination, diff,this); // TODO: This is not good enough, maybe is some null then it will fail or map not null values
            return diff;
        }

        throw new InvalidOperationException("No mapping configuration exists for these types");
    }
    
    public IMappingExpression GetMappingExpression(Type sourceType, Type destinationType, Type diff)
    {
        var key = GetMappingKey(sourceType, destinationType, diff);
        if (_mappings.TryGetValue(key, out var expression))
        {
            return expression;
        }

        throw new InvalidOperationException("No mapping configuration exists for these types");
    }

    private static Expression<Func<TDestination, object>> CreatePropertyAccessor<TDestination>(string propertyName)
    {
        var parameter = Expression.Parameter(typeof(TDestination), "dest");
        var property = Expression.PropertyOrField(parameter, propertyName);

        var propertyAsObject = Expression.Convert(property, typeof(object));
        var lambda = Expression.Lambda<Func<TDestination, object>>(propertyAsObject, parameter);

        return lambda;
    }
    
    private static string GetMappingKey(Type sourceType, Type destinationType, Type diff) =>
        $"{sourceType.FullName}->{destinationType.FullName}->{diff.FullName}";
}

public class PropertyMapping
{
    public Delegate SourceDelegate { get; set; }
    public Delegate CompareDelegate { get; set; }
    
    public string DiffCollectionComparisonTargetKey { get; set; }
    public Delegate SourceCollectionComparingDelegate { get; set; }
    public Delegate DestinationCollectionComparingDelegate { get; set; }
}

public class MappingExpression<TSource, TDestination, TDiff> : IMappingExpression
{
    private readonly Dictionary<string, PropertyMapping> _propertyMappings = new();

    public void ForMember<TProperty>(
        Expression<Func<TDiff, object>> diffProperty,
        Func<TSource, TProperty> propertyMapFunctionSourceProperty,
        Func<TDestination, TProperty> propertyMapFunctionDestinationProperty)
    {
        var memberName = GetMemberName(diffProperty);
        _propertyMappings[memberName] = new PropertyMapping()
        {
            SourceDelegate = propertyMapFunctionSourceProperty,
            CompareDelegate = propertyMapFunctionDestinationProperty
        };
    }

    public void ForMembers<TDiffProperty, TSourceProperty, TDestinationProperty, TKey>(
        Expression<Func<TDiff, IEnumerable<TDiffProperty>>> diffProperty,
        Func<TSource, IEnumerable<TSourceProperty>> sourceCollectionSelector,
        Func<TDestination, IEnumerable<TDestinationProperty>> destinationCollectionSelector,
        Expression<Func<TDiffProperty, TKey>> diffKeySelector,
        Func<TSourceProperty, TKey> sourceKeySelector,
        Func<TDestinationProperty, TKey> destinationKeySelector)
    {
        var memberName = GetMemberName(diffProperty);
        var diffPropertySelector = GetMemberName(diffKeySelector);
        _propertyMappings[memberName] = new PropertyMapping()
        {
            SourceDelegate = sourceCollectionSelector,
            CompareDelegate = destinationCollectionSelector,
            DiffCollectionComparisonTargetKey = diffPropertySelector,
            SourceCollectionComparingDelegate = sourceKeySelector,
            DestinationCollectionComparingDelegate = destinationKeySelector
        };
    }

    public void Map(object source, object destination, object diff, EntityComparer configuration)
    {
        foreach (var diffProperty in typeof(TDiff).GetProperties())
        {
            if (IsPropertyTypeIsPrimitive(diffProperty) &&
                _propertyMappings.TryGetValue(diffProperty.Name, out var mapFunction))
            {
                ComparePrimitiveType(source, destination, diff, mapFunction, diffProperty);
            }
            else if (_propertyMappings.TryGetValue(diffProperty.Name, out var mapFunctionComplexObjects))
            {
                var diffPropertyType = diffProperty.PropertyType;

                var sourceValue = mapFunctionComplexObjects.SourceDelegate.DynamicInvoke(source);
                var destinationValue = mapFunctionComplexObjects.CompareDelegate.DynamicInvoke(destination);

                if (sourceValue != null && destinationValue != null) // TODO: This is not good enough, source value can be null
                {
                    if (diffPropertyType.IsGenericType && diffPropertyType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        var sourceKeySelector = mapFunctionComplexObjects.SourceCollectionComparingDelegate;
                        var destinationKeySelector = mapFunctionComplexObjects.DestinationCollectionComparingDelegate;
                        
                        var sourceArray = (IEnumerable<dynamic>)sourceValue;
                        var destinationArray = (IEnumerable<dynamic>)destinationValue;

                        var diffGenericType = diffPropertyType.GetGenericArguments()[0];
                        var listType = typeof(List<>).MakeGenericType(diffGenericType);
                        var diffArray = (IList)Activator.CreateInstance(listType);

                        var innerMappingExpression =
                            configuration.GetMappingExpression(
                                sourceValue.GetType().GetGenericArguments()[0],
                                destinationValue.GetType().GetGenericArguments()[0],
                                diffPropertyType.GetGenericArguments()[0]);

                        var result = CompareLists(sourceArray, destinationArray, sourceKeySelector, destinationKeySelector);

                        foreach (var addedObject in result.Added)
                        {
                            var destinationElement = Activator.CreateInstance(diffPropertyType.GetGenericArguments()[0]);
                            var sourceElement =
                                Activator.CreateInstance(sourceValue.GetType().GetGenericArguments()[0]);
                            innerMappingExpression.Map(sourceElement, addedObject, destinationElement, configuration);

                            diffArray.Add(destinationElement);
                        }

                        foreach (var destModified in result.Modified)
                        {
                            var sourceElement = sourceArray.FirstOrDefault(src => sourceKeySelector.DynamicInvoke(src) == destinationKeySelector.DynamicInvoke(destModified));

                            if (innerMappingExpression != null && sourceElement != null)
                            {
                                var destinationElement = Activator.CreateInstance(diffPropertyType.GetGenericArguments()[0]);
                                innerMappingExpression.Map(sourceElement, destModified, destinationElement, configuration);

                                var member = mapFunctionComplexObjects.DiffCollectionComparisonTargetKey;
                                PropertyInfo propertyInfo = diffPropertyType.GetGenericArguments()[0].GetProperty(member);
                                propertyInfo.SetValue(destinationElement, destinationKeySelector.DynamicInvoke(destModified));

                                diffArray.Add(destinationElement);
                            }
                        }

                        diffProperty.SetValue(diff, diffArray);
                    }
                    else // Handle other complex properties
                    {
                        var innerMappingExpression =
                            configuration.GetMappingExpression(
                                sourceValue.GetType(),
                                destinationValue.GetType(),
                                diffPropertyType);


                        var diffValue = Activator.CreateInstance(diffPropertyType);

                        innerMappingExpression.Map(sourceValue, destinationValue, diffValue!, configuration);
                        diffProperty.SetValue(diff, diffValue);
                    }
                }
            }
        }
    }

    private static bool IsPropertyTypeIsPrimitive(PropertyInfo diffProperty)
    {
        var nullableType = Nullable.GetUnderlyingType(diffProperty.PropertyType);
        
        return IsPropertyTypeIsPrimitive(nullableType ?? diffProperty.PropertyType);
    }
    
    private static bool IsPropertyTypeIsPrimitive(Type type)
    {
        return (type.IsPrimitive ||
                type.IsEnum ||
                type == typeof(string) ||
                type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) ||
                type == typeof(TimeOnly) ||
                type == typeof(Guid));
    }

    private static void ComparePrimitiveType(
        object source,
        object destination,
        object diff,
        PropertyMapping mapFunction,
        PropertyInfo diffProperty)
    {
        var sourceValue = mapFunction.SourceDelegate.DynamicInvoke(source);
        var newValue = mapFunction.CompareDelegate.DynamicInvoke(destination);

        diffProperty.SetValue(diff, Equals(sourceValue, newValue) ? null : newValue);
    }

    private static string GetMemberName<TDiff, TProperty>(Expression<Func<TDiff, TProperty>> diffProperty)
    {
        var body = diffProperty.Body;

        if (body is UnaryExpression unaryExpression)
        {
            if (unaryExpression.NodeType == ExpressionType.Convert ||
                unaryExpression.NodeType == ExpressionType.ConvertChecked)
            {
                body = unaryExpression.Operand;
            }
        }

        if (body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        throw new InvalidOperationException("Expression is not a member access");
    }

    private class ComparisonResult
    {
        public IEnumerable<dynamic> Added { get; set; }
        public IEnumerable<dynamic> Modified { get; set; }
    }

    private static ComparisonResult CompareLists(IEnumerable<dynamic> source, IEnumerable<dynamic> destination, Delegate sourceKeySelector, Delegate destinationKeySelector)
    {
        var added = destination.Where(dst => source.All(src => destinationKeySelector.DynamicInvoke(dst) != sourceKeySelector.DynamicInvoke(src)));
        var modified = destination.Where(dst => source.Any(src => destinationKeySelector.DynamicInvoke(dst) == sourceKeySelector.DynamicInvoke(src)));

        return new ComparisonResult
        {
            Added = added,
            Modified = modified
        };
    }
}