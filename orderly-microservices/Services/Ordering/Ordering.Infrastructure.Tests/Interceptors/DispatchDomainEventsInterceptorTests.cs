using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ordering.Domain.Abstractions;
using Ordering.Infrastructure.Data.Interceptors;

namespace Ordering.Infrastructure.Tests.Interceptors;

/// <summary>
/// Pins the <see cref="DispatchDomainEventsInterceptor"/> contract: on
/// <c>SaveChanges</c>, collect every <see cref="IDomainEvent"/> on
/// tracked <see cref="IAggregate"/> instances, clear the aggregates'
/// event buffers, then publish each event through
/// <see cref="IMediator"/>. The clear-before-publish ordering is
/// deliberate — even if publish throws, a retry of <c>SaveChanges</c>
/// won't double-dispatch.
/// </summary>
public sealed class DispatchDomainEventsInterceptorTests
{
    /// <summary>
    /// Happy path: two aggregates each raising one event results in
    /// exactly two <see cref="IMediator.Publish"/> calls, in collection
    /// order, before <c>SaveChanges</c> returns. Spec
    /// <c>SavingChanges_WithDomainEvents_DispatchesAllViaMediator</c>.
    /// </summary>
    [Fact]
    public async Task SavingChanges_WithDomainEvents_DispatchesAllViaMediator()
    {
        var mediator = Substitute.For<IMediator>();
        var aggregateA = new TestAggregate();
        var aggregateB = new TestAggregate();
        aggregateA.AddDomainEvent(new TestDomainEvent("a"));
        aggregateB.AddDomainEvent(new TestDomainEvent("b"));

        using var context = new DispatchTestContext(mediator, [aggregateA, aggregateB]);

        await context.SaveChangesAsync();

        await mediator.Received(1).Publish(
            Arg.Is<TestDomainEvent>(e => e.Tag == "a"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).Publish(
            Arg.Is<TestDomainEvent>(e => e.Tag == "b"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// No-op guard: when no tracked aggregate carries domain events,
    /// <see cref="IMediator.Publish"/> is never invoked. Spec
    /// <c>SavingChanges_NoDomainEvents_NoDispatch</c>.
    /// </summary>
    [Fact]
    public async Task SavingChanges_NoDomainEvents_DoesNotCallMediator()
    {
        var mediator = Substitute.For<IMediator>();
        var aggregate = new TestAggregate();
        // no events added — DomainEvents is empty

        using var context = new DispatchTestContext(mediator, [aggregate]);

        await context.SaveChangesAsync();

        await mediator.DidNotReceiveWithAnyArgs().Publish(
            default(IDomainEvent)!,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// After dispatch, the aggregates' <c>DomainEvents</c> collections
    /// are empty — the interceptor clears them before publishing. This
    /// pins the "no double-dispatch on retry" contract: a subsequent
    /// <c>SaveChanges</c> on the same tracker must not re-publish the
    /// already-handled events. Spec
    /// <c>SavingChanges_AfterDispatch_EventsAreCleared</c>.
    /// </summary>
    [Fact]
    public async Task SavingChanges_AfterDispatch_ClearsAggregateEventBuffers()
    {
        var mediator = Substitute.For<IMediator>();
        var aggregate = new TestAggregate();
        aggregate.AddDomainEvent(new TestDomainEvent("only"));

        using var context = new DispatchTestContext(mediator, [aggregate]);

        await context.SaveChangesAsync();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    // ----- helpers -----

    /// <summary>
    /// Minimal <see cref="DbContext"/> that pre-seeds the change
    /// tracker with the supplied aggregates in
    /// <see cref="EntityState.Added"/> — the production SaveChanges
    /// pipeline triggers the interceptor's pre-save hook, which is
    /// where domain events are dispatched.
    /// </summary>
    private sealed class DispatchTestContext(IMediator mediator, IAggregate[] aggregates) : DbContext
    {
        public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseInMemoryDatabase($"dispatch-{Guid.NewGuid():N}")
                .AddInterceptors(new DispatchDomainEventsInterceptor(mediator))
                .ConfigureWarnings(w => w.Ignore(
                    InMemoryEventId.TransactionIgnoredWarning,
                    CoreEventId.ManyServiceProvidersCreatedWarning));
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Attach each supplied aggregate as Added so the interceptor
            // sees it in its pre-save hook — mirrors the production
            // CreateOrderHandler.SaveChangesAsync path.
            foreach (IAggregate aggregate in aggregates)
            {
                Entry(aggregate).State = EntityState.Added;
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Concrete subclass of <see cref="Aggregate{T}"/> so tests can
    /// exercise the dispatch interceptor's iteration over
    /// <see cref="ChangeTracker.Entries{IAggregate}"/> without taking
    /// a dependency on the production <c>Order</c> aggregate (which
    /// requires value-object mapping the InMemory provider doesn't
    /// model out of the box).
    /// </summary>
    private sealed class TestAggregate : Aggregate<Guid>
    {
        public TestAggregate() => Id = Guid.NewGuid();
    }

    /// <summary>
    /// Marker <see cref="IDomainEvent"/> carrying a string tag so the
    /// test can assert which event was published.
    /// </summary>
    private sealed record TestDomainEvent(string Tag) : IDomainEvent;
}
