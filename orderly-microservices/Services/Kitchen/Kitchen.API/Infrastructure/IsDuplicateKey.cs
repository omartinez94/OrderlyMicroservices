using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kitchen.API.Infrastructure;

/// <summary>
/// Helpers for detecting a unique-constraint violation surfaced from
/// PostgreSQL through EF Core. Used by inbound integration-event consumers
/// to make their <c>AddAsync</c>+<c>SaveChangesAsync</c> pair idempotent
/// when two broker redeliveries race past the optimistic
/// <c>GetByIdAsync</c> pre-check.
/// </summary>
public static class IsDuplicateKey
{
    /// <summary>
    /// Returns <c>true</c> when the EF Core exception wraps a
    /// PostgreSQL <c>unique_violation</c> (SQLSTATE <c>23505</c>). The
    /// helper is intentionally PG-only — Kitchen uses Npgsql per
    /// <c>Kitchen.API.csproj:15</c>; a future engine swap is a runtime
    /// concern, not a compile-time one.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505";
}
