using System.Collections.Generic;
using Backtester.Optimization;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of <see cref="ParameterSet"/>: reading a point in the Parameter space back by name, and
    /// enumerating its whole name→value assignment for display (e.g. a leaderboard's Parameter columns).
    /// </summary>
    public class ParameterSetTests
    {
        [Fact]
        public void Values_ExposesEachNameValueAssignment()
        {
            // Key: Parameter name -> the value chosen for this point in the space.
            Dictionary<string, object> assignment = new() { ["fast"] = 10, ["slow"] = 30m };
            ParameterSet set = new(assignment);

            Assert.Equal(2, set.Values.Count);
            Assert.Equal(10, set.Values["fast"]);
            Assert.Equal(30m, set.Values["slow"]);
        }
    }
}
