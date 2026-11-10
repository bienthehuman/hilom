using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace Hilom.Api.Configuration;

/// <summary>
/// The API writes enums as snake_case (<c>data_change</c>), and the OpenAPI contract lists them that way, but
/// minimal APIs bind query-string enums by their C# names (<c>DataChange</c>). This rewrites snake_case query
/// values to the C# name for every enum parameter of the matched endpoint, so <c>?kind=data_change</c> binds.
/// Unknown values are left alone and fail binding with a 400.
/// </summary>
internal sealed class SnakeCaseEnumQuery(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<MethodInfo, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Cache = new();

    public Task InvokeAsync(HttpContext context)
    {
        var method = context.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>();
        if (method is null || context.Request.Query.Count == 0)
        {
            return next(context);
        }

        var enumParameters = Cache.GetOrAdd(method, EnumParametersOf);
        if (enumParameters.Count == 0)
        {
            return next(context);
        }

        var query = new Dictionary<string, StringValues>(context.Request.Query, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var (name, names) in enumParameters)
        {
            if (query.TryGetValue(name, out var raw) && raw.Count == 1 && names.TryGetValue(raw[0]!, out var memberName))
            {
                query[name] = memberName;
                changed = true;
            }
        }

        if (changed)
        {
            context.Request.Query = new QueryCollection(query);
        }

        return next(context);
    }

    /// <summary>For each enum parameter: snake_case value → C# member name.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EnumParametersOf(MethodInfo method)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in method.GetParameters())
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            if (type.IsEnum && parameter.Name is not null)
            {
                result[parameter.Name] = Enum.GetNames(type)
                    .ToDictionary(n => JsonNamingPolicy.SnakeCaseLower.ConvertName(n), n => n, StringComparer.Ordinal);
            }
        }

        return result;
    }
}
