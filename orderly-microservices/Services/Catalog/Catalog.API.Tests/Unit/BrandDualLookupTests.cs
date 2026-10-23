using BuildingBlocks.Entities.Interfaces;
using BuildingBlocks.Identifiers;
using Catalog.API.Exceptions;
using Catalog.API.Features.Brands.GetBrandById;
using Catalog.API.Models;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Unit;

public sealed class BrandDualLookupTests
{
    [Fact]
    public void Brand_ImplementsIShortIdentifiable()
    {
        Brand brand = new()
        {
            Id = Guid.NewGuid(),
            ShortId = "brd7f9k2m4x",
            Name = "Acme Brand"
        };

        brand.Should().BeAssignableTo<IShortIdentifiable>();
        brand.ShortId.Should().Be("brd7f9k2m4x");
    }

    [Fact]
    public void GetBrandByIdQuery_WithGuid_PopulatesIdentifierAndIdProperty()
    {
        Guid guid = Guid.NewGuid();
        GetBrandByIdQuery query = new(guid);

        query.Identifier.Should().Be(guid.ToString());
        query.Id.Should().Be(guid);
    }

    [Fact]
    public void GetBrandByIdQuery_WithShortId_PopulatesIdentifierAndEmptyGuid()
    {
        string shortId = "brd7f9k2m4x";
        GetBrandByIdQuery query = new(shortId);

        query.Identifier.Should().Be(shortId);
        query.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void BrandNotFoundException_WithGuid_FormatsCorrectMessage()
    {
        Guid guid = Guid.NewGuid();
        BrandNotFoundException exception = new(guid);

        exception.Message.Should().Contain("Brand");
        exception.Message.Should().Contain(guid.ToString());
    }

    [Fact]
    public void BrandNotFoundException_WithShortId_FormatsCorrectMessage()
    {
        string shortId = "brd7f9k2m4x";
        BrandNotFoundException exception = new(shortId);

        exception.Message.Should().Contain("Brand");
        exception.Message.Should().Contain(shortId);
    }

    [Fact]
    public void Brand_ShortIdGenerator_GeneratesCompliantPrefix()
    {
        string shortId = ShortIdGenerator.Generate(EntityPrefix.Brand);

        shortId.Should().StartWith(EntityPrefixConstants.Brand);
        shortId.Length.Should().Be(11);
        ShortIdGenerator.IsValid(shortId, EntityPrefix.Brand).Should().BeTrue();
    }
}
