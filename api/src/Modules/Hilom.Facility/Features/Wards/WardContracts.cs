using System.Globalization;
using FluentValidation;
using Hilom.Facility.Domain;

namespace Hilom.Facility.Features.Wards;

/// <param name="Capacity">Active beds in an active ward. Capacity isn't stored anywhere else.</param>
/// <param name="BedCount">All beds, including out-of-service ones.</param>
public sealed record WardSummaryResponse(
    Guid Id,
    string Code,
    string Name,
    WardType WardType,
    DepartmentInfo? Department,
    bool IsActive,
    int Capacity,
    int BedCount);

public sealed record BedResponse(Guid Id, string Label, bool IsActive, uint Version);

/// <param name="Version">Send back when saving; a mismatch means someone else changed the ward meanwhile.</param>
public sealed record WardResponse(
    Guid Id,
    string Code,
    string Name,
    WardType WardType,
    DepartmentInfo? Department,
    bool IsActive,
    int Capacity,
    uint Version,
    IReadOnlyList<BedResponse> Beds);

public enum WardStatusFilter
{
    Active,
    Inactive,
}

public interface IWardFields
{
    string Code { get; }

    string Name { get; }

    WardType? WardType { get; }

    Guid? DepartmentId { get; }
}

public sealed record CreateWardRequest(string Code, string Name, WardType? WardType, Guid? DepartmentId) : IWardFields;

public sealed record UpdateWardRequest(string Code, string Name, WardType? WardType, Guid? DepartmentId, uint Version) : IWardFields;

public sealed record CreateBedRequest(string Label);

public sealed record UpdateBedRequest(string Label, uint Version);

internal static class WardRules
{
    /// <summary>"W2" before "W10", "Bed 9" before "Bed 10".</summary>
    public static readonly StringComparer Natural = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering | CompareOptions.IgnoreCase);

    public static void AddWardRules<T>(this AbstractValidator<T> validator)
        where T : IWardFields
    {
        validator.RuleFor(r => r.Code)
            .NotEmpty().WithMessage("Enter a short code, like W3.")
            .MaximumLength(Ward.CodeMaxLength)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Use letters, numbers and dashes only.");
        validator.RuleFor(r => r.Name).NotEmpty().WithMessage("Enter the ward's name.").MaximumLength(Ward.NameMaxLength);
        validator.RuleFor(r => r.WardType).NotNull().WithMessage("Choose a type.");
    }
}

internal sealed class CreateWardRequestValidator : AbstractValidator<CreateWardRequest>
{
    public CreateWardRequestValidator() => this.AddWardRules();
}

internal sealed class UpdateWardRequestValidator : AbstractValidator<UpdateWardRequest>
{
    public UpdateWardRequestValidator() => this.AddWardRules();
}

internal sealed class CreateBedRequestValidator : AbstractValidator<CreateBedRequest>
{
    public CreateBedRequestValidator() =>
        RuleFor(r => r.Label).NotEmpty().WithMessage("Enter a bed label.").MaximumLength(Bed.LabelMaxLength);
}

internal sealed class UpdateBedRequestValidator : AbstractValidator<UpdateBedRequest>
{
    public UpdateBedRequestValidator() =>
        RuleFor(r => r.Label).NotEmpty().WithMessage("Enter a bed label.").MaximumLength(Bed.LabelMaxLength);
}
