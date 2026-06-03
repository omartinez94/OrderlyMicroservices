namespace Ordering.Application.Dtos;

public record AddressDto(
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country
);