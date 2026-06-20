using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Backtester.Engine;

namespace Backtester.Report
{
    /// <summary>
    /// Renders a <see cref="ReportModel"/> into a single self-contained HTML file. Thin glue: it
    /// serializes the model to JSON and token-replaces it into an embedded <c>template.html</c>
    /// resource, producing one file with its data inlined that opens from <c>file://</c> with no
    /// external dependencies.
    /// </summary>
    public class HtmlReportWriter
    {
        /// <summary>The placeholder in the template that the serialized model is substituted for.</summary>
        private const string DataToken = "__REPORT_DATA__";

        /// <summary>The placeholder in the template that the inlined chart library is substituted for.</summary>
        private const string ChartLibToken = "__CHART_LIB__";

        /// <summary>The embedded template resource name (root namespace + file name).</summary>
        private const string TemplateResource = "Backtester.Report.template.html";

        /// <summary>The embedded Lightweight Charts standalone build (root namespace + file name).</summary>
        private const string ChartLibResource = "Backtester.Report.lightweight-charts.standalone.production.js";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            // camelCase so the template's JavaScript reads the model idiomatically. The default
            // HTML-safe encoder still escapes < > & so the JSON is safe to inline in a <script>.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Builds the complete HTML document straight from a backtest run, projecting it to a report
        /// model internally. The one-call path for callers who only have a <see cref="BacktestResult"/>.
        /// </summary>
        public string BuildHtml(BacktestResult result)
        {
            return BuildHtml(new ReportModelBuilder().Build(result));
        }

        /// <summary>
        /// Writes the HTML report for a backtest run to the file at <paramref name="path"/>, projecting
        /// it to a report model internally. The one-call path for callers who only have a
        /// <see cref="BacktestResult"/>.
        /// </summary>
        public void Write(BacktestResult result, string path)
        {
            Write(new ReportModelBuilder().Build(result), path);
        }

        /// <summary>
        /// Builds the complete HTML document for the given model, with the serialized model inlined.
        /// </summary>
        public string BuildHtml(ReportModel model)
        {
            string template = LoadResource(TemplateResource);
            string json = JsonSerializer.Serialize(model, JsonOptions);

            // Inline the chart library first, then the model data. The library text is fixed and
            // contains no token, so substituting it before the data avoids any collision with the
            // serialized JSON.
            return template
                .Replace(ChartLibToken, LoadResource(ChartLibResource))
                .Replace(DataToken, json);
        }

        /// <summary>
        /// Writes the HTML report for the given model to the file at <paramref name="path"/>.
        /// </summary>
        public void Write(ReportModel model, string path)
        {
            File.WriteAllText(path, BuildHtml(model));
        }

        private static string LoadResource(string resourceName)
        {
            Assembly assembly = typeof(HtmlReportWriter).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
            }

            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}
