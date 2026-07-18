using BuildingBlocks.Behaviors;
using BuildingBlocks.CQRS;
using FluentValidation;
using FluentValidation.Results;

namespace BuildingBlocks.Tests.Unit;

/// <summary>
/// Regression coverage for the Phase-1 BuildingBlocks contribution that
/// relaxes <see cref="ValidationBehavior{TRequest,TResponse}"/>'s generic
/// constraint from <c>ICommand&lt;TResponse&gt;</c> to
/// <c>IRequest&lt;TResponse&gt;</c>.
/// </summary>
/// <remarks>
/// Before the relaxation, any validator registered against an
/// <see cref="IQuery{TResponse}"/>-shaped request was silently skipped because
/// MediatR's open-generic pipeline activation could not instantiate the
/// behavior (the constraint did not match). The relaxation lets queries
/// (Catalog: GetMenuItemsQuery, Ordering: GetOrdersQuery,
/// Discount: EvaluateDiscountRulesQuery) participate in validation.
/// </remarks>
public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task QueryValidator_RunsThroughPipeline()
    {
        var query = new SampleQuery();
        var validator = new CountingQueryValidator();
        var behavior = new ValidationBehavior<SampleQuery, SampleQueryResult>(
            new IValidator<SampleQuery>[] { validator });
        var nextCalled = false;

        await behavior.Handle(
            query,
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(new SampleQueryResult());
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        validator.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task QueryValidator_WithFailures_ThrowsValidationException()
    {
        var behavior = new ValidationBehavior<SampleQuery, SampleQueryResult>(
            new IValidator<SampleQuery>[] { new AlwaysFailingQueryValidator() });

        var act = async () => await behavior.Handle(
            new SampleQuery(),
            _ => Task.FromResult(new SampleQueryResult()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task QueryValidator_WithFailures_DoesNotInvokeHandler()
    {
        var behavior = new ValidationBehavior<SampleQuery, SampleQueryResult>(
            new IValidator<SampleQuery>[] { new AlwaysFailingQueryValidator() });
        var nextCalled = false;

        var act = async () => await behavior.Handle(
            new SampleQuery(),
            _ => { nextCalled = true; return Task.FromResult(new SampleQueryResult()); },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task NoValidatorsRegistered_PassesThroughToHandler()
    {
        var behavior = new ValidationBehavior<SampleQuery, SampleQueryResult>(
            Array.Empty<IValidator<SampleQuery>>());
        var nextCalled = false;

        await behavior.Handle(
            new SampleQuery(),
            _ => { nextCalled = true; return Task.FromResult(new SampleQueryResult()); },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    private sealed record SampleQuery : IQuery<SampleQueryResult>
    {
        public string Value { get; init; } = string.Empty;
    }

    private sealed record SampleQueryResult;

    private sealed class CountingQueryValidator : AbstractValidator<SampleQuery>
    {
        public int InvocationCount { get; private set; }

        public override Task<ValidationResult> ValidateAsync(
            ValidationContext<SampleQuery> context,
            CancellationToken cancellation = default)
        {
            InvocationCount++;
            return base.ValidateAsync(context, cancellation);
        }
    }

    private sealed class AlwaysFailingQueryValidator : AbstractValidator<SampleQuery>
    {
        public AlwaysFailingQueryValidator() =>
            RuleFor(x => x.Value).Must(_ => false).WithMessage("Always fails.");
    }
}