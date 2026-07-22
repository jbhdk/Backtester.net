using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backtester.Report
{
    /// <summary>
    /// Renders an <see cref="OptimizationReportModel"/> into a single self-contained HTML file. Thin glue:
    /// it serializes the model to JSON and token-replaces it into an embedded
    /// <c>optimization-template.html</c> resource, with the Lightweight Charts standalone build inlined,
    /// producing one file that opens from <c>file://</c> with no external dependencies. The optimization
    /// counterpart to <see cref="HtmlReportWriter"/>, a separate report from the winner's single-run one.
    /// </summary>
    public class OptimizationHtmlReportWriter
    {
        /// <summary>The placeholder in the template that the serialized model is substituted for.</summary>
        private const string DataToken = "__OPTIMIZATION_DATA__";

        /// <summary>The placeholder in the template that the inlined chart library is substituted for.</summary>
        private const string ChartLibToken = "__CHART_LIB__";

        /// <summary>The embedded template resource name (root namespace + file name).</summary>
        private const string TemplateResource = "Backtester.Report.optimization-template.html";

        /// <summary>The embedded Lightweight Charts standalone build (root namespace + file name).</summary>
        private const string ChartLibResource = "Backtester.Report.lightweight-charts.standalone.production.js";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            // camelCase so the template's JavaScript reads the model idiomatically. The default
            // HTML-safe encoder still escapes < > & so the JSON is safe to inline in a <script>.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // Any enum reaches the page as its name, matching the single-run report's convention.
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Builds the complete HTML document for the given model, with the serialized model inlined and the
        /// chart library inlined.
        /// </summary>
        public string BuildHtml(OptimizationReportModel model)
        {
            string template = LoadResource(TemplateResource);
            string json = JsonSerializer.Serialize(model, JsonOptions);

            // Inline the chart library first, then the model data. The library text is fixed and contains
            // no token, so substituting it before the data avoids any collision with the serialized JSON.
            return template
                .Replace(ChartLibToken, LoadResource(ChartLibResource))
                .Replace(DataToken, json);
        }

        /// <summary>
        /// Writes the optimization report for the given model to the file at <paramref name="path"/>.
        /// </summary>
        public void Write(OptimizationReportModel model, string path)
        {
            File.WriteAllText(path, BuildHtml(model));
        }

        private static string LoadResource(string resourceName)
        {
            Assembly assembly = typeof(OptimizationHtmlReportWriter).Assembly;
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
