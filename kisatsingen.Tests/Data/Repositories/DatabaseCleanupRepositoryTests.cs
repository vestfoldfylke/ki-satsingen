using System.Reflection;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Cleanup;
using Xunit;

namespace kisatsingen.Tests.Data.Repositories;

public sealed class DatabaseCleanupRepositoryTests
{
    private const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type[] AllowedCallers { get; } = [typeof(DatabaseCleanupBackgroundService)];

    [Fact]
    public void Only_allowed_services_may_use_IDatabaseCleanupRepository()
    {
        var restrictedInterfaceType = typeof(IDatabaseCleanupRepository);

        var violations = GetLoadableTypes(restrictedInterfaceType.Assembly)
            .Where(type => !IsAllowed(type, restrictedInterfaceType, AllowedCallers))
            .SelectMany(type => FindUsages(type, restrictedInterfaceType))
            .ToList();

        Assert.True(violations.Count == 0,
            $"{restrictedInterfaceType.Name} may ONLY be used by " +
            $"{string.Join(", ", AllowedCallers.Select(caller => caller.Name))}. Violations:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindUsages(Type type, Type restrictedType)
    {
        foreach (var ctor in type.GetConstructors(AllMembers))
        {
            foreach (var parameter in ctor.GetParameters())
            {
                if (References(parameter.ParameterType, restrictedType))
                {
                    yield return $"{type.FullName}: constructor parameter '{parameter.Name}'";
                }
            }
        }

        foreach (var method in type.GetMethods(AllMembers))
        {
            foreach (var parameter in method.GetParameters())
            {
                if (References(parameter.ParameterType, restrictedType))
                {
                    yield return $"{type.FullName}.{method.Name}: parameter '{parameter.Name}'";
                }
            }

            if (References(method.ReturnType, restrictedType))
            {
                yield return $"{type.FullName}.{method.Name}: return type";
            }
        }

        foreach (var field in type.GetFields(AllMembers))
        {
            if (References(field.FieldType, restrictedType))
            {
                yield return $"{type.FullName}.{field.Name}: field '{field.Name}'";
            }
        }

        foreach (var property in type.GetProperties(AllMembers))
        {
            if (References(property.PropertyType, restrictedType))
            {
                yield return $"{type.FullName}: property '{property.Name}'";
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }

    private static bool IsAllowed(Type type, Type restrictedType, Type[] allowed)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current == restrictedType || allowed.Contains(current) || restrictedType.IsAssignableFrom(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool References(Type type, Type restrictedType)
    {
        if (restrictedType.IsAssignableFrom(type))
        {
            return true;
        }

        if (type.HasElementType && References(type.GetElementType()!, restrictedType))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(arg => References(arg, restrictedType));
    }
}
