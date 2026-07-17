using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Backtester.Report.Toolkit
{
    /// <summary>
    /// Reflects an attributed settings object into the report's caller-supplied configuration cards.
    /// </summary>
    public class ConfigurationCardBuilder
    {
        /// <summary>The catch-all group holding public properties that carry no <see cref="ReportSettingAttribute"/>.</summary>
        private const string OtherGroup = "Other";

        /// <summary>
        /// Builds one configuration card per distinct group found on the settings object's public properties.
        /// A property carrying <see cref="ReportSettingAttribute"/> uses its curated label and group; a
        /// property without one falls into the catch-all "Other" group, labelled by its property name; a
        /// property carrying <see cref="ReportSettingIgnoreAttribute"/> is excluded from every card. Curated
        /// groups render in the declaration order of their first member, but the "Other" card always renders
        /// last. Rows within a group render in ascending <see cref="ReportSettingAttribute.Order"/>, ties
        /// broken by declaration order, each a two-cell <c>[label, value]</c> list with values formatted
        /// using the invariant culture.
        /// </summary>
        /// <param name="settings">The settings object whose properties describe the run's configuration.</param>
        /// <returns>The configuration cards: curated groups in declaration order, then the "Other" catch-all last.</returns>
        public IReadOnlyList<ReportCard> Build(object settings)
        {
            // Key: group name. Value: the group's accumulated (order, row) pairs in declaration order, so a
            // later stable sort by order can tie-break on declaration order. Later members of the same group
            // append to the list opened by the group's first member.
            Dictionary<string, List<(int Order, IReadOnlyList<string> Row)>> rowsByGroup = new();

            // Cards in first-appearance (declaration) order, tracked separately from the lookup above because
            // properties are reflected in declaration order.
            List<ReportCard> cards = new();

            foreach (PropertyInfo property in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Opt-out wins over everything: an ignored property appears in no card, not even "Other",
                // even when it also carries a display attribute.
                if (property.GetCustomAttribute<ReportSettingIgnoreAttribute>() != null)
                {
                    continue;
                }

                ReportSettingAttribute setting = property.GetCustomAttribute<ReportSettingAttribute>();

                // Un-attributed properties are the drift-safety catch-all: they surface in the "Other" group,
                // labelled by their property name, so a newly added property is never silently omitted.
                string group = setting != null ? setting.Group : OtherGroup;
                string label = setting != null ? setting.Label : property.Name;

                // Un-attributed catch-all rows have no author-supplied order; the default 0 leaves them in
                // declaration order, matching a curated row that leaves Order at its default.
                int order = setting != null ? setting.Order : 0;

                if (!rowsByGroup.TryGetValue(group, out List<(int Order, IReadOnlyList<string> Row)> rows))
                {
                    rows = new List<(int, IReadOnlyList<string>)>();
                    rowsByGroup.Add(group, rows);
                    cards.Add(new ReportCard { Title = group, Headers = null, Rows = null });
                }

                rows.Add((order, new List<string> { label, Format(property.GetValue(settings)) }));
            }

            // Within each group, rows render in ascending author-supplied order. OrderBy is a stable sort, so
            // rows sharing an order keep the declaration order they were accumulated in.
            foreach (ReportCard card in cards)
            {
                card.Rows = rowsByGroup[card.Title]
                    .OrderBy(entry => entry.Order)
                    .Select(entry => entry.Row)
                    .ToList();
            }

            // The catch-all card always renders last, regardless of where its first member appears in
            // declaration order, so the curated groups keep their intended order and drift stays visible.
            int otherIndex = cards.FindIndex(card => card.Title == OtherGroup);
            if (otherIndex >= 0 && otherIndex != cards.Count - 1)
            {
                ReportCard other = cards[otherIndex];
                cards.RemoveAt(otherIndex);
                cards.Add(other);
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
