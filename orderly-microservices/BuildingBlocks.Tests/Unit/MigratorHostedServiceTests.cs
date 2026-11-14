using System.Net.Sockets;
using BuildingBlocks.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BuildingBlocks.Tests.Unit;

public sealed class MigratorHostedServiceTests
{
    [Fact]
    public void IsTransient_WithPostgresException_ReturnsTrue()
    {
        PostgresException ex = new("transient error", "ERROR", "ERROR", "57P03");

        bool result = TestMigratorHostedService.CallIsTransient(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsTransient_WithBaseNpgsqlException_ReturnsTrue()
    {
        NpgsqlException ex = new("Failed to connect to 127.0.0.1:5432", new SocketException((int)SocketError.ConnectionRefused));

        bool result = TestMigratorHostedService.CallIsTransient(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsTransient_WithEfCoreRetryWrappedNpgsqlException_ReturnsTrue()
    {
        NpgsqlException inner = new("Failed to connect to 127.0.0.1:5432", new SocketException((int)SocketError.ConnectionRefused));
        InvalidOperationException wrapped = new(
            "An exception has been raised that is likely due to a transient failure.",
            inner);

        bool result = TestMigratorHostedService.CallIsTransient(wrapped);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsTransient_WithNonTransientException_ReturnsFalse()
    {
        InvalidOperationException ex = new("Schema mismatch or invalid configuration.");

        bool result = TestMigratorHostedService.CallIsTransient(ex);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotCreateContext()
    {
        IOptions<MigratorHostedServiceOptions> options = Options.Create(new MigratorHostedServiceOptions
        {
            Enabled = false,
        });
        TestMigratorHostedService sut = new(null!, options);

        await sut.StartAsync(CancellationToken.None);

        sut.CreateContextCallCount.Should().Be(0);
    }

#pragma warning disable CA1812 // Internal class is never instantiated
    private sealed class DummyDbContext(DbContextOptions<DummyDbContext> options) : DbContext(options);
#pragma warning restore CA1812

    private sealed class TestMigratorHostedService(
        IServiceProvider services,
        IOptions<MigratorHostedServiceOptions> options)
        : MigratorHostedService<DummyDbContext>(
            services,
            options,
            NullLogger<MigratorHostedService<DummyDbContext>>.Instance)
    {
        public int CreateContextCallCount { get; private set; }

        public static bool CallIsTransient(Exception ex) => IsTransient(ex);

        protected override DummyDbContext CreateContext(IServiceProvider serviceProvider)
        {
            CreateContextCallCount++;
            throw new NotSupportedException("Should not be called when disabled.");
        }
    }
}
