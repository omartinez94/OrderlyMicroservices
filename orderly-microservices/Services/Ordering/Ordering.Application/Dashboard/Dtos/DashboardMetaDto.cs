namespace Ordering.Application.Dashboard.Dtos;

public record DashboardMetaDto(
    IReadOnlyDictionary<string, string> Sections,
    Instant GeneratedAt);
