using System.Collections.Generic;

namespace Backtester.Optimization
{
    /// <summary>
    /// One complete assignment of values to the varied Parameters — a single point in the
    /// <see cref="ParameterSpace"/>. Values are read back by name with the accessor matching the axis
    /// type it was declared with.
    /// </summary>
    public class ParameterSet
    {
        // Key: Parameter name -> the value chosen for this point in the space (boxed int, decimal, or bool).
        private readonly IReadOnlyDictionary<string, object> _values;

        /// <summary>Initializes a new Parameter set from the given name-to-value assignment.</summary>
        public ParameterSet(IReadOnlyDictionary<string, object> values)
        {
            _values = values;
        }

        /// <summary>Returns the value of the named integer Parameter.</summary>
        public int Int(string name)
        {
            return (int)Value(name);
        }

        /// <summary>Returns the value of the named decimal Parameter.</summary>
        public decimal Decimal(string name)
        {
            return (decimal)Value(name);
        }

        /// <summary>Returns the value of the named boolean Parameter.</summary>
        public bool Bool(string name)
        {
            return (bool)Value(name);
        }

        private object Value(string name)
        {
            if (!_values.TryGetValue(name, out object value))
            {
                throw new KeyNotFoundException($"No Parameter named '{name}' in this Parameter set.");
            }

            return value;
        }
    }
}
