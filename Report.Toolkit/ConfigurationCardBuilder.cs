using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Backtester.Report.Toolkit
{
    /// <summary>
    /// Reflects an attributed settings object into the report's caller-supplied configuration cards.
    /// </summary>
    public class ConfigurationCardBuilder
    {
        /// <summary>
        /// Builds one configuration card per distinct group found on the settings object's attributed
        /// properties. Groups render in the declaration order of their first member; rows within a group
        /// render in declaration order. Each row is a two-cell <c>[label, value]</c> list, with values
        /// formatted using the invariant culture.
        /// </summary>
        /// <param name="settings">The settings object whose properties describe the run's configuration.</param>
        /// <returns>The configuration cards, one per group, in group declaration order.</returns>
        public IReadOnlyList<ReportCard> Build(object settings)
        {
            // Key: group name. Value: the mutable row list backing that group's card, so later members of
            // the same group append to the card opened by the group's first member.
            Dictionary<string, List<IReadOnlyList<string>>> rowsByGroup = new();

            // Cards in first-appearance (declaration) order, tracked separately from the lookup above because
            // properties are reflected in declaration order.
            List<ReportCard> cards = new();

            foreach (PropertyInfo property in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                ReportSettingAttribute setting = property.GetCustomAttribute<ReportSettingAttribute>();
                if (setting == null)
                {
                    continue;
                }

                if (!rowsByGroup.TryGetValue(setting.Group, out List<IReadOnlyList<string>> rows))
                {
                    rows = new List<IReadOnlyList<string>>();
                    rowsByGroup.Add(setting.Group, rows);
                    cards.Add(new ReportCard { Title = setting.Group, Headers = null, Rows = rows });
                }

                rows.Add(new List<string> { setting.Label, Format(property.GetValue(settings)) });
            }

            return cards;
        }

        /// <summary>
        /// Formats a setting value for display: null becomes an empty string, everything else is rendered
        /// with the invariant culture (booleans as invariant <c>True</c>/<c>False</c>).
        /// </summary>
        /// <param name="value">The raw property value.</param>
        /// <returns>The invariant-culture display text.</returns>
        private static string Format(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return System.Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
