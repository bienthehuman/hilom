namespace Hilom.Api.Configuration;

internal static class DotEnvConfiguration
{
    /// <summary>
    /// Adds values from the repository's <c>.env</c> file (found by walking up from the working directory).
    /// Real environment variables always win.
    /// </summary>
    public static IConfigurationBuilder AddDotEnvFile(this IConfigurationBuilder configuration)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in DotNetEnv.Env.NoEnvVars().TraversePath().Load())
        {
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                values[key] = value;
            }
        }

        return configuration.AddInMemoryCollection(values);
    }
}
