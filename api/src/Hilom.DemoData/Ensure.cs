using Hilom.SharedKernel.Results;

namespace Hilom.DemoData;

internal static class Ensure
{
    /// <summary>
    /// The generators only ask the domain for things that are allowed. If a rule says no, that's a bug here, so it stops
    /// the run with the rule's own message instead of writing data the product would never have produced.
    /// </summary>
    public static T Ok<T>(Result<T> result) => result.IsSuccess
        ? result.Value
        : throw new InvalidOperationException($"Demo data broke a domain rule: {result.Error.Code}: {result.Error.Message}");

    public static void Ok(Result result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Demo data broke a domain rule: {result.Error.Code}: {result.Error.Message}");
        }
    }
}
