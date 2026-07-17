using System;

namespace Backtester.Report.Toolkit
{
    /// <summary>
    /// Marks a settings property for exclusion from the report's configuration cards. A property carrying
    /// this attribute appears in no card, not even the catch-all "Other" group, and the exclusion wins even
    /// when the property also carries a <see cref="ReportSettingAttribute"/>. Kept separate from the display
    /// attribute so that presentation and opt-out stay independent concerns.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ReportSettingIgnoreAttribute : Attribute
    {
    }
}
