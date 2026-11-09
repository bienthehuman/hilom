using Hilom.Api.ErrorHandling;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.UnitTests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void Success_carries_a_value()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Failure_carries_an_error_and_no_value()
    {
        var error = Error.Conflict("slot_already_booked", "That slot was just booked.");
        var result = Result.Failure<int>(error);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(error);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.BusinessRule, 422)]
    public void Error_types_map_to_http_status_codes(ErrorType type, int status)
    {
        ErrorResults.StatusCodeFor(type).ShouldBe(status);
    }
}

public sealed class EntityTests
{
    private sealed class Thing : Entity;

    [Fact]
    public void New_entities_get_a_uuid_v7_id()
    {
        var first = new Thing();
        var second = new Thing();

        first.Id.Version.ShouldBe(7);
        second.Id.ShouldNotBe(first.Id);
    }
}
