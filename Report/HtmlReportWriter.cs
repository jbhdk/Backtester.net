using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

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

        /// <summary>The embedded template resource name (root namespace + file name).</summary>
        private const string TemplateResource = "Backtester.Report.template.html";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            // camelCase so the template's JavaScript reads the model idiomatically. The default
            // HTML-safe encoder still escapes < > & so the JSON is safe to inline in a <script>.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Builds the complete HTML document for the given model, with the serialized model inlined.
        /// </summary>
        public string BuildHtml(ReportModel model)
        {
            string template = LoadTemplate();
            string json = JsonSerializer.Serialize(model, JsonOptions);
            return template.Replace(DataToken, json);
        }

        /// <summary>
        /// Writes the HTML report for the given model to the file at <paramref name="path"/>.
        /// </summary>
        public void Write(ReportModel model, string path)
        {
            File.WriteAllText(path, BuildHtml(model));
        }

        private static string LoadTemplate()
        {
            Assembly assembly = typeof(HtmlReportWriter).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(TemplateResource);
            if (stream == null)
            {
                throw new InvalidOperationException($"Embedded template resource '{TemplateResource}' was not found.");
            }

            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}
