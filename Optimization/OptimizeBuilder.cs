using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Strategies;

namespace Backtester.Optimization
{
    /// <summary>
    /// The fluent builder returned by <see cref="Optimize.For{TParameters}"/>. It offers two authoring
    /// paths into the same primitives — a <see cref="ParameterSpace"/> and an adapting Trial factory,
    /// bundled as an <see cref="OptimizationSetup"/>: <see cref="FromAttributes"/> reflects the
    /// <c>[Optimize]</c>-decorated Parameters into axes, while <see cref="Vary{TValue}"/> names a Parameter
    /// by expression selector and adds its axis explicitly, terminating with <see cref="Build"/>.
    /// </summary>
    /// <typeparam name="TParameters">The parameters type whose properties define the axes.</typeparam>
    public class OptimizeBuilder<TParameters>
    {
        private readonly TParameters _instance;
        private readonly Func<TParameters, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> _factory;

        // The axes accumulated by fluent .Vary() calls, in call order. Each pairs the selected property with
        // the action that adds its range to a ParameterSpace; Build() bundles them into the setup.
        private readonly List<(PropertyInfo Property, Action<ParameterSpace> AddTo)> _variedAxes = new();

        /// <summary>Initializes a new builder over the bound parameters instance and Trial factory.</summary>
        public OptimizeBuilder(
            TParameters instance,
            Func<TParameters, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> factory)
        {
            _instance = instance;
            _factory = factory;
        }

        /// <summary>
        /// Builds the <see cref="OptimizationSetup"/> from the instance's <c>[Optimize]</c> Parameters. An
        /// <see cref="int"/> or <see cref="decimal"/> Parameter expands over its <c>(min, max, step)</c>
        /// range; a parameterless <c>[Optimize]</c> on a <see cref="bool"/> expands to <c>{ false, true }</c>.
        /// The adapting Trial factory clones the bound instance for each Parameter set, sets the swept
        /// properties (including <c>init</c>-only ones), and calls the strongly-typed factory with the clone.
        /// </summary>
        public OptimizationSetup FromAttributes()
        {
            List<(PropertyInfo Property, Action<ParameterSpace> AddTo)> axes = new();
            foreach (PropertyInfo property in OptimizedProperties())
            {
                axes.Add((property, space => AddAttributeAxis(space, property)));
            }

            return BuildSetup(axes);
        }

        /// <summary>
        /// Adds an axis for the Parameter named by <paramref name="selector"/>, taking values from
        /// <paramref name="from"/> to <paramref name="to"/> inclusive in increments of
        /// <paramref name="step"/>. Multiple calls compose into the cartesian product of their axes. The
        /// selected property type must be <see cref="int"/> or <see cref="decimal"/>. Terminate with
        /// <see cref="Build"/>.
        /// </summary>
        /// <typeparam name="TValue">The selected property's type, inferred from the selector.</typeparam>
        /// <param name="selector">Names the Parameter to vary, e.g. <c>parameters => parameters.StopAtrMultiple</c>.</param>
        public OptimizeBuilder<TParameters> Vary<TValue>(
            Expression<Func<TParameters, TValue>> selector, TValue from, TValue to, TValue step)
        {
            PropertyInfo property = PropertyOf(selector);
            _variedAxes.Add((property, space => AddRangeAxis(space, property, from, to, step)));
            return this;
        }

        /// <summary>
        /// Builds the <see cref="OptimizationSetup"/> from the axes accumulated by <see cref="Vary{TValue}"/>.
        /// The adapting Trial factory behaves exactly as the attributes path: it clones the bound instance for
        /// each Parameter set, sets the varied properties (including <c>init</c>-only ones), and calls the
        /// strongly-typed factory with the clone.
        /// </summary>
        public OptimizationSetup Build()
        {
            return BuildSetup(_variedAxes);
        }

        /// <summary>
        /// Bundles a set of axes into an <see cref="OptimizationSetup"/>: adds each axis to the Parameter
        /// space, rejects any axis the factory never consumes, and wires the adapting Trial factory that
        /// realizes each Parameter set onto a typed clone. Shared by both authoring paths.
        /// </summary>
        private OptimizationSetup BuildSetup(List<(PropertyInfo Property, Action<ParameterSpace> AddTo)> axes)
        {
            ParameterSpace space = new();
            foreach ((PropertyInfo Property, Action<ParameterSpace> AddTo) axis in axes)
            {
                axis.AddTo(space);
            }

            RejectUnconsumedParameters(axes);

            Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> trialFactory =
                (parameters, portfolio) => _factory(Realize(axes, parameters), portfolio);

            return new OptimizationSetup(space, trialFactory);
        }

        /// <summary>The instance's public properties carrying an <see cref="OptimizeAttribute"/>, in declaration order.</summary>
        private static PropertyInfo[] OptimizedProperties()
        {
            List<PropertyInfo> optimized = new();
            foreach (PropertyInfo property in typeof(TParameters).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<OptimizeAttribute>() != null)
                {
                    optimized.Add(property);
                }
            }

            return optimized.ToArray();
        }

        /// <summary>Adds the axis for one <c>[Optimize]</c> property, keyed by the property name and typed by the property type.</summary>
        private static void AddAttributeAxis(ParameterSpace space, PropertyInfo property)
        {
            OptimizeAttribute attribute = property.GetCustomAttribute<OptimizeAttribute>();

            if (property.PropertyType == typeof(bool))
            {
                space.AddBool(property.Name);
            }
            else if (property.PropertyType == typeof(int))
            {
                space.AddInt(property.Name, (int)attribute.Min, (int)attribute.Max, (int)attribute.Step);
            }
            else if (property.PropertyType == typeof(decimal))
            {
                space.AddDecimal(property.Name, (decimal)attribute.Min, (decimal)attribute.Max, (decimal)attribute.Step);
            }
            else
            {
                throw new NotSupportedException(
                    $"[Optimize] Parameter '{property.Name}' has unsupported type {property.PropertyType.Name}; only int, decimal, and bool are supported.");
            }
        }

        /// <summary>Adds a numeric axis over an explicit <c>(from, to, step)</c> range for a varied property.</summary>
        private static void AddRangeAxis(ParameterSpace space, PropertyInfo property, object from, object to, object step)
        {
            if (property.PropertyType == typeof(int))
            {
                space.AddInt(
                    property.Name,
                    Convert.ToInt32(from, CultureInfo.InvariantCulture),
                    Convert.ToInt32(to, CultureInfo.InvariantCulture),
                    Convert.ToInt32(step, CultureInfo.InvariantCulture));
            }
            else if (property.PropertyType == typeof(decimal))
            {
                space.AddDecimal(
                    property.Name,
                    Convert.ToDecimal(from, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(to, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(step, CultureInfo.InvariantCulture));
            }
            else
            {
                throw new NotSupportedException(
                    $".Vary() Parameter '{property.Name}' has unsupported type {property.PropertyType.Name}; only int and decimal are supported.");
            }
        }

        /// <summary>Extracts the property a <c>.Vary()</c> selector names, unwrapping the boxing conversion the compiler inserts for value types.</summary>
        private static PropertyInfo PropertyOf<TValue>(Expression<Func<TParameters, TValue>> selector)
        {
            Expression body = selector.Body;
            if (body is UnaryExpression conversion && conversion.NodeType == ExpressionType.Convert)
            {
                body = conversion.Operand;
            }

            if (body is MemberExpression member && member.Member is PropertyInfo property)
            {
                return property;
            }

            throw new ArgumentException(
                "The .Vary() selector must name a property, e.g. parameters => parameters.StopAtrMultiple.", nameof(selector));
        }

        /// <summary>Clones the bound instance and sets the swept properties from the Parameter set, yielding the typed clone the factory reads.</summary>
        private TParameters Realize(List<(PropertyInfo Property, Action<ParameterSpace> AddTo)> axes, ParameterSet parameters)
        {
            TParameters clone = Clone(_instance);
            foreach ((PropertyInfo Property, Action<ParameterSpace> AddTo) axis in axes)
            {
                axis.Property.SetValue(clone, SweptValue(parameters, axis.Property));
            }

            return clone;
        }

        /// <summary>Reads the swept value for one property from the Parameter set using the accessor matching the property type.</summary>
        private static object SweptValue(ParameterSet parameters, PropertyInfo property)
        {
            if (property.PropertyType == typeof(bool))
            {
                return parameters.Bool(property.Name);
            }

            if (property.PropertyType == typeof(int))
            {
                return parameters.Int(property.Name);
            }

            return parameters.Decimal(property.Name);
        }

        /// <summary>
        /// Rejects any axis the factory never consumes — an axis that would silently produce identical
        /// Trials. For each axis it builds the strategy and broker twice, holding every other axis at its
        /// first value and setting this one to its two extremes, then fingerprints the produced objects: if
        /// varying the axis changes nothing, the Parameter is inert and is rejected.
        /// </summary>
        private void RejectUnconsumedParameters(List<(PropertyInfo Property, Action<ParameterSpace> AddTo)> axes)
        {
            foreach ((PropertyInfo Property, Action<ParameterSpace> AddTo) axis in axes)
            {
                (object first, object last) = AxisEndpoints(axis);
                if (Equals(first, last))
                {
                    // A single-value axis cannot vary, so it can neither be a silent no-op nor be detected as one.
                    continue;
                }

                // One shared Portfolio so the two builds differ only by the axis under test, not by their broker's portfolio.
                Portfolio portfolio = new(100_000m);
                (IStrategy Strategy, IBrokerSimulator Broker) low = _factory(ProbeClone(axes, axis.Property, first), portfolio);
                (IStrategy Strategy, IBrokerSimulator Broker) high = _factory(ProbeClone(axes, axis.Property, last), portfolio);

                if (Fingerprint(low.Strategy, low.Broker) == Fingerprint(high.Strategy, high.Broker))
                {
                    throw new InvalidOperationException(
                        $"Parameter '{axis.Property.Name}' is declared but never consumed by the factory; varying it would produce identical Trials. Read it in the factory or stop varying it.");
                }
            }
        }

        /// <summary>The first and last values of an axis, reusing <see cref="ParameterSpace"/> so the stepping matches exactly.</summary>
        private static (object First, object Last) AxisEndpoints((PropertyInfo Property, Action<ParameterSpace> AddTo) axis)
        {
            ParameterSpace single = new();
            axis.AddTo(single);
            IReadOnlyList<ParameterSet> values = single.Expand();
            return (SweptValue(values[0], axis.Property), SweptValue(values[values.Count - 1], axis.Property));
        }

        /// <summary>A probe clone with every axis held at its first value except <paramref name="varying"/>, set to <paramref name="value"/>.</summary>
        private TParameters ProbeClone(List<(PropertyInfo Property, Action<ParameterSpace> AddTo)> axes, PropertyInfo varying, object value)
        {
            TParameters clone = Clone(_instance);
            foreach ((PropertyInfo Property, Action<ParameterSpace> AddTo) axis in axes)
            {
                axis.Property.SetValue(clone, axis.Property == varying ? value : AxisEndpoints(axis).First);
            }

            return clone;
        }

        /// <summary>
        /// A structural fingerprint of the constructed objects: every instance field is walked up the
        /// hierarchy to its primitive leaves (cycle-guarded and depth-bounded), so two builds compare equal
        /// only when the axis under test left the strategy and broker in identical state.
        /// </summary>
        private static string Fingerprint(params object[] roots)
        {
            StringBuilder builder = new();
            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            foreach (object root in roots)
            {
                Append(builder, root, visited, depth: 0);
            }

            return builder.ToString();
        }

        /// <summary>The maximum depth the fingerprint descends, bounding the walk over the constructed object graph.</summary>
        private const int MaxFingerprintDepth = 6;

        /// <summary>Appends one value to the fingerprint: a leaf as its invariant text, otherwise its fields recursively.</summary>
        private static void Append(StringBuilder builder, object value, HashSet<object> visited, int depth)
        {
            if (value == null)
            {
                builder.Append("null;");
                return;
            }

            Type type = value.GetType();
            if (IsLeaf(type))
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';');
                return;
            }

            // A depth cap and a reference-identity visited set keep the walk finite over deep or cyclic graphs.
            if (depth >= MaxFingerprintDepth || !visited.Add(value))
            {
                return;
            }

            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    builder.Append(field.Name).Append('=');
                    Append(builder, field.GetValue(value), visited, depth + 1);
                }
            }
        }

        /// <summary>Whether a type is a fingerprint leaf: rendered directly rather than walked field-by-field.</summary>
        private static bool IsLeaf(Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);
        }

        /// <summary>
        /// Produces a field-for-field clone of the source, independent of its constructor shape: the clone is
        /// allocated uninitialized and every instance field up the hierarchy is copied, so non-optimized
        /// Parameters carry through and the swept ones can then be overwritten (including <c>init</c>-only ones).
        /// </summary>
        private static TParameters Clone(TParameters source)
        {
            object clone = RuntimeHelpers.GetUninitializedObject(typeof(TParameters));
            for (Type type = typeof(TParameters); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    field.SetValue(clone, field.GetValue(source));
                }
            }

            return (TParameters)clone;
        }
    }
}
