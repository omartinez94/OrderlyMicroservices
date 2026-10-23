using System.Collections.Concurrent;
using BuildingBlocks.Identifiers;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Tests.Unit;

public sealed class ShortIdGeneratorTests
{
    [Theory]
    [InlineData(EntityPrefix.Restaurant, EntityPrefixConstants.Restaurant)]
    [InlineData(EntityPrefix.Order, EntityPrefixConstants.Order)]
    [InlineData(EntityPrefix.Brand, EntityPrefixConstants.Brand)]
    [InlineData(EntityPrefix.Table, EntityPrefixConstants.Table)]
    [InlineData(EntityPrefix.MenuItem, EntityPrefixConstants.MenuItem)]
    [InlineData(EntityPrefix.MenuCategory, EntityPrefixConstants.MenuCategory)]
    [InlineData(EntityPrefix.Reservation, EntityPrefixConstants.Reservation)]
    [InlineData(EntityPrefix.WalkInQueue, EntityPrefixConstants.WalkInQueue)]
    [InlineData(EntityPrefix.User, EntityPrefixConstants.User)]
    [InlineData(EntityPrefix.KitchenStation, EntityPrefixConstants.KitchenStation)]
    public void Generate_WithEntityPrefix_ReturnsContiguousTokenWithExpectedPrefix(EntityPrefix prefix, string expectedPrefix)
    {
        ArgumentNullException.ThrowIfNull(expectedPrefix);
        string shortId = ShortIdGenerator.Generate(prefix);

        shortId.Should().StartWith(expectedPrefix);
        shortId.Length.Should().Be(expectedPrefix.Length + ShortIdGenerator.DefaultRandomLength);
        shortId.Should().NotContain("_");
        shortId.Should().NotContain("-");
    }

    [Theory]
    [InlineData(8, 11)]
    [InlineData(9, 12)]
    [InlineData(10, 13)]
    public void Generate_WithVariableLength_ReturnsExpectedTotalLength(int randomLength, int expectedTotalLength)
    {
        string shortId = ShortIdGenerator.Generate(EntityPrefix.Restaurant, randomLength);

        shortId.Length.Should().Be(expectedTotalLength);
        shortId.Should().StartWith("rst");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Generate_WithOutOfRangeLength_ThrowsArgumentOutOfRangeException(int invalidLength)
    {
        Action act = () => ShortIdGenerator.Generate(EntityPrefix.Restaurant, invalidLength);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_WithInvalidStringPrefix_ThrowsArgumentException(string? invalidPrefix)
    {
        Action act = () => ShortIdGenerator.Generate(invalidPrefix!, 8);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Alphabet_DoesNotContainVisuallyConfusableCharacters()
    {
        char[] confusable = ['0', 'O', '1', 'l', 'I', 'o', 'i'];

        foreach (char c in confusable)
        {
            ShortIdGenerator.Alphabet.Should().NotContain(c.ToString());
        }
    }

    [Fact]
    public void Generate_ContainsOnlyAllowedAlphabetCharactersInSuffix()
    {
        for (int i = 0; i < 100; i++)
        {
            string shortId = ShortIdGenerator.Generate(EntityPrefix.Restaurant, 10);
            string suffix = shortId[3..];

            foreach (char c in suffix)
            {
                ShortIdGenerator.Alphabet.Should().Contain(c.ToString());
            }
        }
    }

    [Fact]
    public void Generate_ProducesUniqueTokens()
    {
        const int count = 5000;
        HashSet<string> generated = new(count);

        for (int i = 0; i < count; i++)
        {
            string id = ShortIdGenerator.Generate(EntityPrefix.Restaurant);
            generated.Add(id).Should().BeTrue($"Collision detected at iteration {i}: {id}");
        }

        generated.Count.Should().Be(count);
    }

    [Fact]
    public void Generate_IsThreadSafe_UnderConcurrentExecution()
    {
        const int count = 5000;
        ConcurrentDictionary<string, byte> generated = new();

        Parallel.For(0, count, _ =>
        {
            string id = ShortIdGenerator.Generate(EntityPrefix.Order);
            bool added = generated.TryAdd(id, 0);
            added.Should().BeTrue();
        });

        generated.Count.Should().Be(count);
    }

    [Fact]
    public void IsValid_WithMatchingPrefix_ReturnsTrue()
    {
        string shortId = ShortIdGenerator.Generate(EntityPrefix.Restaurant);

        ShortIdGenerator.IsValid(shortId, EntityPrefix.Restaurant).Should().BeTrue();
        ShortIdGenerator.IsValid(shortId, EntityPrefix.Order).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("rst123")] // too short
    [InlineData("rst01loI89ab")] // contains confusable chars 0, 1, l, o, I
    [InlineData("rst_k8j3x9mq")] // contains underscore
    public void IsValid_WithInvalidInputs_ReturnsFalse(string? candidate)
    {
        ShortIdGenerator.IsValid(candidate).Should().BeFalse();
    }

    [Fact]
    public void TryExtractPrefix_IdentifiesKnownPrefixes()
    {
        string shortId = ShortIdGenerator.Generate(EntityPrefix.Table);

        bool success = ShortIdGenerator.TryExtractPrefix(shortId, out EntityPrefix prefix);

        success.Should().BeTrue();
        prefix.Should().Be(EntityPrefix.Table);
    }

    [Fact]
    public void TryExtractPrefix_WithUnknownPrefix_ReturnsFalse()
    {
        bool success = ShortIdGenerator.TryExtractPrefix("xyz8k2m9p4w", out _);

        success.Should().BeFalse();
    }
}
