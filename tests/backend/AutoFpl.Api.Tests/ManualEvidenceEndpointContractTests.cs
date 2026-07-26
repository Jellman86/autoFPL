using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using AutoFpl.Api.Mcp;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace AutoFpl.Api.Tests;

public sealed class ManualEvidenceEndpointContractTests
{
    private static readonly string CatalogPath = Path.Combine(
        AppContext.BaseDirectory,
        "contracts",
        "manual-evidence",
        "v1",
        "current-post-routes.json");

    [Fact]
    public void Catalog_request_types_and_fields_match_actual_json_bindings()
    {
        using var factory = new WebApplicationFactory<Program>();
        using HttpClient client = factory.CreateClient();
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(CatalogPath));
        JsonSerializerOptions serializerOptions = factory.Services
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value
            .SerializerOptions;
        IReadOnlyDictionary<string, JsonElement> catalogRoutes = catalog.RootElement
            .GetProperty("routes")
            .EnumerateArray()
            .ToDictionary(
                route => route.GetProperty("path").GetString()!,
                route => route,
                StringComparer.Ordinal);

        RouteEndpoint[] postEndpoints = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata
                .GetMetadata<HttpMethodMetadata>()?
                .HttpMethods
                .Contains("POST", StringComparer.Ordinal) is true)
            .Where(endpoint => endpoint.Metadata
                .GetMetadata<MachineProtocolEndpointMetadata>() is null)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(catalogRoutes.Keys.Order(), postEndpoints
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .Order());

        foreach (RouteEndpoint endpoint in postEndpoints)
        {
            string route = endpoint.RoutePattern.RawText!;
            Type[] boundRequestTypes = endpoint.Metadata
                .GetOrderedMetadata<MethodInfo>()
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Where(type => type.Name.EndsWith("Request", StringComparison.Ordinal))
                .Distinct()
                .ToArray();

            Type boundRequestType = Assert.Single(boundRequestTypes);
            JsonElement catalogRoute = catalogRoutes[route];
            Assert.Equal(
                catalogRoute.GetProperty("requestType").GetString(),
                boundRequestType.FullName ?? boundRequestType.Name);

            string[] actualFields = GetAcceptedJsonPropertyPaths(
                    boundRequestType,
                    serializerOptions)
                .Order()
                .ToArray();
            string[] catalogFields = catalogRoute
                .GetProperty("fields")
                .EnumerateArray()
                .Select(field => field.GetProperty("jsonPath").GetString()!)
                .Order()
                .ToArray();

            Assert.Equal(actualFields, catalogFields);
        }
    }

    [Fact]
    public void Json_field_discovery_uses_web_naming_and_arbitrary_collection_shapes()
    {
        using var factory = new WebApplicationFactory<Program>();
        using HttpClient client = factory.CreateClient();
        JsonSerializerOptions serializerOptions = factory.Services
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value
            .SerializerOptions;

        Assert.Equal(
            ["children", "children[].childValue", "primitiveArray", "unannotatedField"],
            GetAcceptedJsonPropertyPaths(
                    typeof(FutureRequestShape),
                    serializerOptions)
                .Order()
                .ToArray());
    }

    private static IEnumerable<string> GetAcceptedJsonPropertyPaths(
        Type requestType,
        JsonSerializerOptions serializerOptions)
    {
        foreach (PropertyContract property in GetPropertyContracts(
            requestType,
            serializerOptions))
        {
            Assert.False(
                property.IsExtensionData,
                $"{requestType.FullName} accepts unbounded JSON extension data.");
            yield return property.JsonName;

            JsonTypeInfo propertyTypeInfo = serializerOptions.GetTypeInfo(property.PropertyType);
            if (propertyTypeInfo.Kind == JsonTypeInfoKind.Dictionary)
            {
                Assert.Fail(
                    $"{requestType.FullName}.{property.ClrName} accepts unbounded JSON object keys; "
                    + "extend the manual-evidence contract explicitly before adding this shape.");
            }

            Type? elementType = GetEnumerableElementType(property.PropertyType);
            if (elementType is not null)
            {
                JsonTypeInfo elementTypeInfo = serializerOptions.GetTypeInfo(elementType);
                if (elementTypeInfo.Kind != JsonTypeInfoKind.Object)
                {
                    continue;
                }

                foreach (PropertyContract child in GetPropertyContracts(
                    elementType,
                    serializerOptions))
                {
                    Assert.False(
                        child.IsExtensionData,
                        $"{elementType.FullName} accepts unbounded JSON extension data.");
                    AssertJsonLeaf(elementType, child, serializerOptions);
                    yield return $"{property.JsonName}[].{child.JsonName}";
                }

                continue;
            }

            Assert.True(
                propertyTypeInfo.Kind != JsonTypeInfoKind.Object,
                $"{requestType.FullName}.{property.ClrName} adds an unsupported nested object. "
                + "Extend the manual-evidence path contract before adding this shape.");
        }
    }

    private static void AssertJsonLeaf(
        Type declaringType,
        PropertyContract property,
        JsonSerializerOptions serializerOptions)
    {
        JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(property.PropertyType);
        Assert.True(
            typeInfo.Kind is not JsonTypeInfoKind.Object
                and not JsonTypeInfoKind.Dictionary
                && GetEnumerableElementType(property.PropertyType) is null,
            $"{declaringType.FullName}.{property.ClrName} adds unsupported nested JSON depth. "
            + "Extend the manual-evidence path contract before adding this shape.");
    }

    private static IReadOnlyList<PropertyContract> GetPropertyContracts(
        Type type,
        JsonSerializerOptions serializerOptions)
    {
        JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(type);
        if (typeInfo.Kind == JsonTypeInfoKind.Object)
        {
            return typeInfo.Properties
                .Select(property => new PropertyContract(
                    property.Name,
                    property.AttributeProvider is PropertyInfo propertyInfo
                        ? propertyInfo.Name
                        : property.Name,
                    property.PropertyType,
                    property.IsExtensionData))
                .ToArray();
        }

        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition
                != JsonIgnoreCondition.Always)
            .Select(property => new PropertyContract(
                property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? serializerOptions.PropertyNamingPolicy?.ConvertName(property.Name)
                    ?? property.Name,
                property.Name,
                property.PropertyType,
                property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null))
            .ToArray();
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        Type? enumerableType = type
            .GetInterfaces()
            .Prepend(type)
            .FirstOrDefault(candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerableType?.GetGenericArguments()[0];
    }

    private sealed record FutureRequestShape(
        int UnannotatedField,
        int[] PrimitiveArray,
        List<FutureChildShape> Children);

    private sealed record FutureChildShape(int ChildValue);

    private sealed record PropertyContract(
        string JsonName,
        string ClrName,
        Type PropertyType,
        bool IsExtensionData);
}
