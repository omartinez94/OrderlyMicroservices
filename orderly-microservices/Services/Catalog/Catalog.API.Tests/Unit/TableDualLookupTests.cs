using BuildingBlocks.Entities.Interfaces;
using BuildingBlocks.Identifiers;
using Catalog.API.Exceptions;
using Catalog.API.Features.Tables.GetTableById;
using Catalog.API.Models;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Unit;

public sealed class TableDualLookupTests
{
    [Fact]
    public void Table_ImplementsIShortIdentifiable()
    {
        Table table = new()
        {
            Id = Guid.NewGuid(),
            ShortId = "tbl4m8k2p9x",
            TableNumber = "T-1"
        };

        table.Should().BeAssignableTo<IShortIdentifiable>();
        table.ShortId.Should().Be("tbl4m8k2p9x");
    }

    [Fact]
    public void GetTableByIdQuery_WithGuid_PopulatesIdentifierAndIdProperty()
    {
        Guid guid = Guid.NewGuid();
        GetTableByIdQuery query = new(guid);

        query.Identifier.Should().Be(guid.ToString());
        query.Id.Should().Be(guid);
    }

    [Fact]
    public void GetTableByIdQuery_WithShortId_PopulatesIdentifierAndEmptyGuid()
    {
        string shortId = "tbl4m8k2p9x";
        GetTableByIdQuery query = new(shortId);

        query.Identifier.Should().Be(shortId);
        query.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TableNotFoundException_WithGuid_FormatsCorrectMessage()
    {
        Guid guid = Guid.NewGuid();
        TableNotFoundException exception = new(guid);

        exception.Message.Should().Contain("Table");
        exception.Message.Should().Contain(guid.ToString());
    }

    [Fact]
    public void TableNotFoundException_WithShortId_FormatsCorrectMessage()
    {
        string shortId = "tbl4m8k2p9x";
        TableNotFoundException exception = new(shortId);

        exception.Message.Should().Contain("Table");
        exception.Message.Should().Contain(shortId);
    }

    [Fact]
    public void Table_ShortIdGenerator_GeneratesCompliantPrefix()
    {
        string shortId = ShortIdGenerator.Generate(EntityPrefix.Table);

        shortId.Should().StartWith(EntityPrefixConstants.Table);
        shortId.Length.Should().Be(11);
        ShortIdGenerator.IsValid(shortId, EntityPrefix.Table).Should().BeTrue();
    }
}
