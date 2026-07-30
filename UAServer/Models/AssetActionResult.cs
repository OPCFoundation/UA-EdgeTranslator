
namespace Opc.Ua.Edge.Translator.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of an asynchronous WoT action invocation on an asset.
    /// </summary>
    /// <remarks>
    /// The synchronous <c>ExecuteAction</c> used a <c>ref IList&lt;object&gt; outputArgs</c>
    /// parameter so that drivers could replace the caller's list. <c>ref</c> parameters are
    /// not allowed on async methods, so drivers now return the output arguments alongside
    /// the status string instead.
    /// </remarks>
    public class AssetActionResult
    {
        /// <summary>
        /// Driver-specific status string describing the outcome of the action.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// The output arguments produced by the action, or <c>null</c> when the action
        /// does not produce any.
        /// </summary>
        public IList<object> Outputs { get; set; }

        /// <summary>
        /// Creates a result carrying only a status string and no output arguments.
        /// </summary>
        public static AssetActionResult FromStatus(string status)
        {
            return new AssetActionResult { Status = status };
        }

        /// <summary>
        /// Creates a result carrying a status string and the given output arguments.
        /// </summary>
        public static AssetActionResult FromOutputs(string status, IList<object> outputs)
        {
            return new AssetActionResult { Status = status, Outputs = outputs };
        }
    }
}
