using System.Security.Claims;
using BuildingBlocks.Entities.Interfaces;
using BuildingBlocks.Entities.Interceptors;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NodaTime;

namespace Ordering.Infrastructure.Tests.Interceptors;

/// <summary>
/// Pins the <see cref="AuditableEntityInterceptor"/> contract: on
/// <c>SaveChanges</c>, iterate <c>ChangeTracker.Entries&lt;IAuditableEntity&gt;()</c>
/// and stamp <c>CreatedFrom</c> on <see cref="EntityState.Added"/> rows +
/// <c>ModifiedFrom</c> on <see cref="EntityState.Modified"/> rows. The
/// user id is read from the <see cref="IHttpContextAccessor"/>; when no
/// principal is in scope the interceptor falls back to <c>"system"</c>.
/// Ordering has no <c>IDeletableEntity</c> contract, so the
/// <c>SavingChanges_DeletedEntity_SetsDeletedAtAndDeletedBy</c> spec
/// from §6.6 is replaced with a non-auditable-entity guard — see the
/// deviation log.
/// </summary>
public sealed class AuditableEntityInterceptorTests
{
    /// <summary>
    /// Happy path: <c>SaveChanges</c> on a new
    /// <see cref="IAuditableEntity"/> stamps <c>CreatedBy</c> from the
    /// HTTP principal and <c>CreatedAt</c> from
    /// <see cref="SystemClock"/>. Spec
    /// <c>SavingChanges_NewEntity_SetsCreatedAtAndCreatedBy</c>.
    /// </summary>
    [Fact]
    public void SavingChanges_NewAuditableEntity_StampsCreatedByAndCreatedAt()
    {
        var accessor = new StubAccessor("staff-123");

        using var context = new AuditableTestContext(accessor);
        var entity = new TestAuditableEntity { Id = Guid.NewGuid() };

        context.Auditables.Add(entity);

        var before = SystemClock.Instance.GetCurrentInstant();
        context.SaveChanges();
        var after = SystemClock.Instance.GetCurrentInstant();

        entity.CreatedBy.Should().Be("staff-123");
        entity.CreatedAt.Should().BeGreaterThanOrEqualTo(before).And.BeLessThanOrEqualTo(after);
        entity.LastModifiedBy.Should().Be("staff-123");
        entity.LastModifiedAt.Should().NotBeNull();
    }

    /// <summary>
    /// <c>SaveChanges</c> on a previously-saved entity (now
    /// <see cref="EntityState.Modified"/>) stamps <c>LastModifiedBy</c>
    /// and <c>LastModifiedAt</c> while leaving <c>CreatedBy</c> /
    /// <c>CreatedAt</c> intact. Spec
    /// <c>SavingChanges_ModifiedEntity_SetsLastModifiedAtAndLastModifiedBy</c>.
    /// </summary>
    [Fact]
    public void SavingChanges_ModifiedAuditableEntity_StampsLastModifiedAndLeavesCreatedIntact()
    {
        // One context, two SaveChanges — the InMemory provider gives each
        // context its own store, so a second context would not see the
        // first's writes. The interceptor stamps "creator" on the first
        // save, then we mutate the entity on the same context (now
        // EntityState.Modified) and stamp "editor" on the second save.
        // To switch the principal between the two saves we rebind the
        // service provider that supplies IHttpContextAccessor — the
        // interceptor reads it on every SaveChanges call.
        var accessor = new SwitchableAccessor();
        using var context = new AuditableTestContext(accessor)
        {
            HttpAccessorProvider = () => accessor.Current,
        };
        var entity = new TestAuditableEntity { Id = Guid.NewGuid() };

        accessor.Set("creator");
        context.Auditables.Add(entity);
        context.SaveChanges();

        var capturedCreatedBy = entity.CreatedBy;
        var capturedCreatedAt = entity.CreatedAt;

        accessor.Set("editor");
        // Mutate a property so EF Core's change tracker flips the row
        // to EntityState.Modified on the next SaveChanges.
        entity.Name = "updated";

        var before = SystemClock.Instance.GetCurrentInstant();
        context.SaveChanges();
        var after = SystemClock.Instance.GetCurrentInstant();

        entity.CreatedBy.Should().Be(capturedCreatedBy);
        entity.CreatedAt.Should().Be(capturedCreatedAt);
        entity.LastModifiedBy.Should().Be("editor");
        entity.LastModifiedAt.Should().NotBeNull();
        entity.LastModifiedAt!.Value.Should().BeGreaterThanOrEqualTo(before).And.BeLessThanOrEqualTo(after);
    }

    /// <summary>
    /// The interceptor MUST skip entities that do not implement
    /// <see cref="IAuditableEntity"/> — the iteration is over
    /// <c>Entries&lt;IAuditableEntity&gt;</c>, so a plain
    /// <see cref="TestNonAuditableEntity"/> is invisible to the audit
    /// stamping. Replaces the spec's
    /// <c>SavingChanges_DeletedEntity_SetsDeletedAtAndDeletedBy</c>
    /// (Ordering has no <c>IDeletableEntity</c>) with the more relevant
    /// "non-auditable entity is ignored" contract.
    /// </summary>
    [Fact]
    public void SavingChanges_NonAuditableEntity_IsIgnoredByInterceptor()
    {
        var accessor = new StubAccessor("should-not-be-stamped");

        using var context = new AuditableTestContext(accessor);
        var plain = new TestNonAuditableEntity { Id = Guid.NewGuid() };
        context.NonAuditables.Add(plain);

        var act = () => context.SaveChanges();

        act.Should().NotThrow();
    }

    /// <summary>
    /// When no <see cref="HttpContext"/> is in scope the interceptor
    /// falls back to the literal user id <c>"system"</c> rather than
    /// throwing — production-side migrations, hosted services, and the
    /// outbox dispatcher all run with no HTTP principal and rely on
    /// this fallback.
    /// </summary>
    [Fact]
    public void SavingChanges_NoHttpContext_DefaultsUserIdToSystem()
    {
        var accessor = new StubAccessor(userId: null);

        using var context = new AuditableTestContext(accessor);
        context.Auditables.Add(new TestAuditableEntity { Id = Guid.NewGuid() });

        context.SaveChanges();

        var saved = context.Auditables.Single();
        saved.CreatedBy.Should().Be("system");
    }

    /// <summary>
    /// No-op guard: when the change tracker has no
    /// <see cref="IAuditableEntity"/> entries (or no entries at all),
    /// <c>SaveChanges</c> still completes cleanly and the interceptor
    /// does not throw. Spec
    /// <c>SavingChanges_NoChanges_NoAuditFieldsSet</c>.
    /// </summary>
    [Fact]
    public void SavingChanges_NoTrackedAuditableEntities_IsNoOp()
    {
        var accessor = new StubAccessor("irrelevant");

        using var context = new AuditableTestContext(accessor);
        context.NonAuditables.Add(new TestNonAuditableEntity { Id = Guid.NewGuid() });

        var act = () => context.SaveChanges();

        act.Should().NotThrow();
    }

    // ----- helpers -----

    /// <summary>
    /// Hand-rolled <see cref="IHttpContextAccessor"/> stub. Avoids
    /// <c>Substitute.For&lt;HttpContext&gt;</c> which fails on the
    /// abstract <c>User</c> property access pattern NSubstitute can't
    /// intercept cleanly. A <c>null</c> <paramref name="userId"/> means
    /// "no HTTP context" — the production interceptor falls back to
    /// <c>"system"</c> for this case. Uses explicit interface
    /// implementation because <see cref="IHttpContextAccessor.HttpContext"/>
    /// has both a getter AND a setter in newer ASP.NET Core; the setter
    /// is a no-op for tests (no production caller assigns to it during
    /// a request).
    /// </summary>
    private sealed class StubAccessor(string? userId) : IHttpContextAccessor
    {
        HttpContext? IHttpContextAccessor.HttpContext
        {
            get => userId is null
                ? null
                : new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")),
                };
            set { /* no-op — test stub; production callers don't write */ }
        }
    }

    /// <summary>
    /// Mutable principal source — let a single test simulate two
    /// different HTTP users across two SaveChanges calls. The
    /// interceptor reads <see cref="IHttpContextAccessor"/> on every
    /// save, so flipping <see cref="Set"/> mid-test exercises the
    /// "creator → editor" audit transition.
    /// </summary>
    private sealed class SwitchableAccessor : IHttpContextAccessor
    {
        public IHttpContextAccessor? Current { get; private set; }

        public void Set(string? userId) => Current = new StubAccessor(userId);

        HttpContext? IHttpContextAccessor.HttpContext
        {
            get => ((IHttpContextAccessor?)Current)?.HttpContext;
            set { /* no-op */ }
        }
    }

    /// <summary>
    /// Self-contained <see cref="IAuditableEntity{TId}"/> stub — no
    /// value-object PK or owned types, so the InMemory provider maps it
    /// without the production <c>CustomerConfiguration</c>'s value
    /// converters and <c>OwnsOne</c> walk.
    /// </summary>
    private sealed class TestAuditableEntity : IAuditableEntity<Guid>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public Instant CreatedAt { get; set; }
        public string LastModifiedBy { get; set; } = string.Empty;
        public Instant? LastModifiedAt { get; set; }

        void IAuditableEntity.CreatedFrom(string userId, Instant timestamp)
        {
            CreatedBy = userId;
            CreatedAt = timestamp;
            LastModifiedBy = userId;
            LastModifiedAt = timestamp;
        }

        void IAuditableEntity.ModifiedFrom(string userId, Instant timestamp)
        {
            LastModifiedBy = userId;
            LastModifiedAt = timestamp;
        }
    }

    /// <summary>
    /// Plain entity — not <see cref="IAuditableEntity"/>. The interceptor
    /// must not stamp audit fields on it.
    /// </summary>
    private sealed class TestNonAuditableEntity
    {
        public Guid Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Minimal DbContext exposing both stub entities. The
    /// <see cref="IHttpContextAccessor"/> is supplied via the
    /// <see cref="IInfrastructure"/> seam — exactly how
    /// <c>AuditableEntityInterceptor</c> resolves it in production.
    /// </summary>
    private sealed class AuditableTestContext(IHttpContextAccessor accessor) : DbContext
    {
        /// <summary>
        /// Allows tests to swap the accessor mid-test (e.g. to flip
        /// the user between two <c>SaveChanges</c> calls). When set,
        /// the <see cref="SwitchableAccessor"/> is consulted instead
        /// of the ctor-captured accessor.
        /// </summary>
        public Func<IHttpContextAccessor>? HttpAccessorProvider { get; init; }

        public DbSet<TestAuditableEntity> Auditables => Set<TestAuditableEntity>();
        public DbSet<TestNonAuditableEntity> NonAuditables => Set<TestNonAuditableEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseInMemoryDatabase($"auditable-{Guid.NewGuid():N}")
                .AddInterceptors(new AuditableEntityInterceptor())
                .ConfigureWarnings(w => w.Ignore(
                    InMemoryEventId.TransactionIgnoredWarning,
                    CoreEventId.ManyServiceProvidersCreatedWarning))
                .UseApplicationServiceProvider(new SingleServiceProvider(this, accessor));
        }
    }

    /// <summary>
    /// Resolves only <see cref="IHttpContextAccessor"/>; everything
    /// else returns <c>null</c> (EF Core asks for the accessor and
    /// nothing else during a SaveChanges pipeline). If the context has
    /// an <see cref="AuditableTestContext.HttpAccessorProvider"/>, it
    /// takes precedence so the modified-state test can swap principals
    /// between saves.
    /// </summary>
    private sealed class SingleServiceProvider(AuditableTestContext context, IHttpContextAccessor fallback) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType != typeof(IHttpContextAccessor))
            {
                return null;
            }

            return context.HttpAccessorProvider?.Invoke() ?? fallback;
        }
    }
}
