using System;

namespace Backtester.Report.Toolkit
{
    /// <summary>
    /// Marks a settings property for inclusion in the report's configuration cards, carrying its
    /// human-readable display label, the group (card) it belongs to, and its row order within that group.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ReportSettingAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReportSettingAttribute"/> class.
        /// </summary>
        /// <param name="label">The human-readable display name shown for this setting.</param>
        /// <param name="group">The name of the group (card) this setting belongs to.</param>
        public ReportSettingAttribute(string label, string group)
        {
            Label = label;
            Group = group;
        }

        /// <summary>Gets the human-readable display name shown for this setting.</summary>
        public string Label { get; }

        /// <summary>Gets the name of the group (card) this setting belongs to.</summary>
        public string Group { get; }

        /// <summary>
        /// Gets or sets the row order within the group, ascending. Ties are broken by declaration order.
        /// Defaults to 0.
        /// </summary>
        public int Order { get; set; }
    }
}
