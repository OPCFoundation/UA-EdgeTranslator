using System.Xml;
using System.Xml.Linq;

namespace WotOpcUaMapper.Services
{
    /// <summary>
    /// NodeSet2 files picked together in the "Open NodeSet2 XML" dialog: the top-level
    /// nodeset and, typically, the other files of its folder. A browser never tells the
    /// server which folder a file came from, so picking the folder's files along with the
    /// nodeset is how its dependencies are found there before the Cloud Library is asked.
    /// </summary>
    public sealed class LocalNodesetFiles
    {
        private readonly List<NodesetFile> _files = new();

        private sealed record NodesetFile(string FileName, string Xml, string[] Models, string[] RequiredModels);

        /// <summary>The number of NodeSet2 documents picked.</summary>
        public int Count => _files.Count;

        /// <summary>
        /// Adds a picked file. Returns false, ignoring it, when it is not a NodeSet2 document.
        /// </summary>
        public bool Add(string fileName, string xml)
        {
            XElement? nodeSet;
            try
            {
                nodeSet = XDocument.Parse(xml).Root;
            }
            catch (XmlException)
            {
                return false;
            }

            if (nodeSet?.Name.LocalName != "UANodeSet")
            {
                return false;
            }

            List<XElement> models = nodeSet.Elements()
                .Where(e => e.Name.LocalName == "Models")
                .Elements()
                .Where(e => e.Name.LocalName == "Model")
                .ToList();

            _files.Add(new NodesetFile(
                fileName,
                xml,
                ModelUris(models),
                ModelUris(models.Elements().Where(e => e.Name.LocalName == "RequiredModel"))));
            return true;
        }

        /// <summary>The picked nodeset that defines the model, or null when none does.</summary>
        public string? FindXml(string modelUri) =>
            _files.FirstOrDefault(f => f.Models.Contains(modelUri, StringComparer.Ordinal))?.Xml;

        /// <summary>
        /// The top-level nodeset: the one picked file whose models none of the others require.
        /// Returns null, with the reason in <paramref name="error"/>, when there is no single one.
        /// </summary>
        public string? FindTopLevel(out string fileName, out string? error)
        {
            HashSet<string> required = new(_files.SelectMany(f => f.RequiredModels), StringComparer.Ordinal);
            List<NodesetFile> topLevel = _files.Where(f => !f.Models.Any(required.Contains)).ToList();

            if (topLevel.Count == 1)
            {
                fileName = topLevel[0].FileName;
                error = null;
                return topLevel[0].Xml;
            }

            fileName = string.Empty;
            error = topLevel.Count == 0
                ? "The selected nodesets all require each other, so none of them is the top-level nodeset."
                : $"The selected files contain {topLevel.Count} unrelated top-level nodesets ({string.Join(", ", topLevel.Select(f => f.FileName))}). Select one top-level nodeset together with its dependencies.";
            return null;
        }

        private static string[] ModelUris(IEnumerable<XElement> elements) =>
            elements
                .Select(e => (string?)e.Attribute("ModelUri"))
                .Where(uri => !string.IsNullOrEmpty(uri))
                .Select(uri => uri!)
                .ToArray();
    }
}
