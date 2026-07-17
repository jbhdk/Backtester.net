using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Backtester.Report;
using Backtester.Report.Toolkit;
using Xunit;

namespace BacktesterTests.Report.Toolkit.Tests
{
    /// <summary>
    /// Tests <see cref="ConfigurationCardBuilder"/> through its public <c>Build</c> seam.
    /// </summary>
    public class ConfigurationCardBuilderTests
    {
        [Fact]
        public void Build_PropertiesSharingGroup_ProduceSingleCardTitledWithGroupName()
        {
            // Arrange
            ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
            SampleSettings settings = new SampleSettings { FastPeriod = 12, SlowPeriod = 26 };

            // Act
            IReadOnlyList<ReportCard> cards = builder.Build(settings);

            // Assert
            ReportCard card = Assert.Single(cards);
            Assert.Equal("MACD", card.Title);
            Assert.Null(card.Headers);
            Assert.Equal(
                new[]
                {
                    new[] { "Fast period", "12" },
                    new[] { "Slow period", "26" }
                },
                card.Rows);
        }

        [Fact]
        public void Build_DistinctGroups_ProduceCardsInFirstAppearanceOrder()
        {
            // Arrange
            ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
            InterleavedSettings settings = new InterleavedSettings();

            // Act
            IReadOnlyList<ReportCard> cards = builder.Build(settings);

            // Assert
            Assert.Equal(new[] { "MACD", "Risk" }, new[] { cards[0].Title, cards[1].Title });
        }

        [Fact]
        public void Build_GroupMembersDeclaredApart_ShareOneCardInDeclarationOrder()
        {
            // Arrange
            ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
            InterleavedSettings settings = new InterleavedSettings();

            // Act
            IReadOnlyList<ReportCard> cards = builder.Build(settings);

            // Assert
            ReportCard macd = cards[0];
            Assert.Equal(
                new[] { "Slow period", "Fast period" },
                new[] { macd.Rows[0][0], macd.Rows[1][0] });
        }

        [Fact]
        public void Build_DecimalValue_RendersWithInvariantCultureRegardlessOfCurrentCulture()
        {
            // Arrange
            ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
            TypedSettings settings = new TypedSettings { RiskFraction = 0.006m };
            CultureInfo original = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("da-DK");

                // Act
                IReadOnlyList<ReportCard> cards = builder.Build(settings);

                // Assert
                Assert.Equal("0.006", cards[0].Rows[0][1]);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Fact]
        public void Build_BooleanValue_RendersTrue()
        {
            // Arrange
            ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
            TypedSettings settings = new TypedSettings { Enabled = true };

            // Act
            IReadOnlyList<ReportCard> cards = builder.Build(settings);

            // Assert
            Assert.Equal("True", cards[0].Rows[1][1]);
        }

        [Fact]
        public void Build_FalseBooleanValue_RendersFalse()
        {
            // Arrange
            ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
            TypedSettings settings = new TypedSettings { Enabled = false };

            // Act
            IReadOnlyList<ReportCard> cards = builder.Build(settings);

            // Assert
            Assert.Equal("False", cards[0].Rows[1][1]);
        }

        [Fact]
        public void Build_NullValue_RendersEmptyString()
        {
            // Arrange
            ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
            TypedSettings settings = new TypedSettings { Note = null };

            // Act
            IReadOnlyList<ReportCard> cards = builder.Build(settings);

            // Assert
            Assert.Equal(string.Empty, cards[0].Rows[2][1]);
        }
    }
}
