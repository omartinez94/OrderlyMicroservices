namespace Ordering.Application.Dashboard.Dtos;

using System.Diagnostics.CodeAnalysis;

[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "ActionUrl is an application-relative SPA route.")]
[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "ActionUrl is an application-relative SPA route.")]
public record OperationalAlertDto(
    string Id,
    Instant Timestamp,
    string Severity,
    string Category,
    string Title,
    string Message,
    string ActionUrl,
    string ActionLabel,
    string EntityId);
