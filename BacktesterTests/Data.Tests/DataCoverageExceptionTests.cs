using System;
using Backtester.Data;
using Xunit;

namespace BacktesterTests.Data.Tests
{
    public class DataCoverageExceptionTests
    {
        [Fact]
        public void Message_NamesPrimingAsRemedy()
        {
            DataCoverageException ex = new("AAPL", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), "1h");

            Assert.Contains("Prime", ex.Message, StringComparison.Ordinal);
        }
    }
}
