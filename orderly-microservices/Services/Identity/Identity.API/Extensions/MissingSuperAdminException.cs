namespace Identity.API.Extensions;

/// <summary>
/// Thrown at startup when a non-Development environment has no
/// <c>SuperAdmin</c> user. The dev seeder
/// (<see cref="DataSeeder"/>) only creates the seed SuperAdmin in
/// <c>IsDevelopment()</c>, so a production deploy that forgets to
/// provision an admin via an out-of-band bootstrap (CLI, migration
/// script, IaC) must fail-fast rather than silently accept the
/// missing role.
/// </summary>
/// <remarks>
/// <para>The exception message lists the remediation runbook (the
/// <c>dotnet user-secrets</c> + manual user-create sequence) so the
/// on-call engineer can resolve without opening the plan doc.</para>
/// </remarks>
public sealed class MissingSuperAdminException : InvalidOperationException
{
    public MissingSuperAdminException(string message) : base(message) { }

    public MissingSuperAdminException(string message, Exception innerException)
        : base(message, innerException) { }
}
