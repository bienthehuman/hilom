using FluentValidation;
using Hilom.Workforce.Domain;

namespace Hilom.Workforce.Features.Lookups;

/// <param name="Version">Send back when saving; a mismatch means someone else changed the entry meanwhile.</param>
public sealed record LookupResponse(Guid Id, string Name, bool IsActive, uint Version)
{
    internal static LookupResponse From(Lookup lookup) => new(lookup.Id, lookup.Name, lookup.IsActive, lookup.Version);
}

public enum LookupStatusFilter
{
    Active,
    Inactive,
}

public sealed record CreateLookupRequest(string Name);

public sealed record UpdateLookupRequest(string Name, uint Version);

internal sealed class CreateLookupRequestValidator : AbstractValidator<CreateLookupRequest>
{
    public CreateLookupRequestValidator() =>
        RuleFor(r => r.Name).NotEmpty().WithMessage("Enter a name.").MaximumLength(Lookup.NameMaxLength);
}

internal sealed class UpdateLookupRequestValidator : AbstractValidator<UpdateLookupRequest>
{
    public UpdateLookupRequestValidator() =>
        RuleFor(r => r.Name).NotEmpty().WithMessage("Enter a name.").MaximumLength(Lookup.NameMaxLength);
}
