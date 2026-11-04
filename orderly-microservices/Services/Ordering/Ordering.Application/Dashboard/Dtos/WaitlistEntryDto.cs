namespace Ordering.Application.Dashboard.Dtos;

public record WaitlistEntryDto(
    Guid EntryId,
    string PartyName,
    int PartySize,
    int WaitMinutes);
