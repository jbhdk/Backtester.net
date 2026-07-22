using System;
using System.Collections.Generic;
using System.Linq;

namespace Backtester.Optimization
{
    /// <summary>
    /// Every Parameter set an Optimization will evaluate: named axes, each an integer range, expanded to
    /// the cartesian product of their values. Axes are added fluently; <see cref="Expand"/> produces the
    /// grid.
    /// </summary>
    public class ParameterSpace
    {
        // The declared axes in declaration order. Each axis pairs a Parameter name with the values it
        // takes; Expand forms the cartesian product across all axes.
        private readonly List<(string Name, IReadOnlyList<object> Values)> _axes = new();

        /// <summary>
        /// Adds an integer Parameter axis taking values from <paramref name="from"/> to
        /// <paramref name="to"/> inclusive in increments of <paramref name="step"/>.
        /// </summary>
        public ParameterSpace AddInt(string name, int from, int to, int step)
        {
            if (step <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive.");
            }

            List<object> values = new();
            for (int value = from; value <= to; value += step)
            {
                values.Add(value);
            }

            _axes.Add((name, values));
            return this;
        }

        /// <summary>
        /// Adds a decimal Parameter axis taking values from <paramref name="from"/> to
        /// <paramref name="to"/> inclusive in increments of <paramref name="step"/>.
        /// </summary>
        public ParameterSpace AddDecimal(string name, decimal from, decimal to, decimal step)
        {
            if (step <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive.");
            }

            List<object> values = new();
            for (decimal value = from; value <= to; value += step)
            {
                values.Add(value);
            }

            _axes.Add((name, values));
            return this;
        }

        /// <summary>Adds a boolean Parameter axis taking both <c>false</c> and <c>true</c>.</summary>
        public ParameterSpace AddBool(string name)
        {
            _axes.Add((name, new object[] { false, true }));
            return this;
        }

        /// <summary>
        /// Expands the declared axes into every Parameter set: the cartesian product of all axes' values.
        /// A space with no axes yields a single empty Parameter set.
        /// </summary>
        public IReadOnlyList<ParameterSet> Expand()
        {
            List<Dictionary<string, object>> assignments = new() { new Dictionary<string, object>() };
            foreach ((string name, IReadOnlyList<object> values) in _axes)
            {
                List<Dictionary<string, object>> next = new();
                foreach (Dictionary<string, object> partial in assignments)
                {
                    foreach (object value in values)
                    {
                        // Key: Parameter name -> the value chosen on this axis for this partial assignment.
                        Dictionary<string, object> combined = new(partial) { [name] = value };
                        next.Add(combined);
                    }
                }

                assignments = next;
            }

            return assignments.Select(assignment => new ParameterSet(assignment)).ToList();
        }
    }
}
