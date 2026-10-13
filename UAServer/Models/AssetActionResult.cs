
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
        /// Whether the action succeeded. A driver that reports success with an arbitrary
        /// message (such as an HTTP response body) sets this, so the message is not taken
        /// for an error. When <c>null</c>, success is inferred from <see cref="Status"/>:
        /// only <c>ok</c> and <c>success</c> count as success, as before.
        /// </summary>
        public bool? Succeeded { get; set; }

        /// <summary>
        /// True when the action succeeded: <see cref="Succeeded"/> when the driver set
        /// it, otherwise a <see cref="Status"/> of <c>ok</c> or <c>success</c>.
        /// </summary>
        public bool IsSuccess =>
            Succeeded ?? (Status != null
                && (Status.Equals("ok", System.StringComparison.OrdinalIgnoreCase)
                    || Status.Equals("success", System.StringComparison.OrdinalIgnoreCase)));

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
        /// Creates a successful result whose <paramref name="message"/> describes the
        /// outcome; the message is not interpreted as an error.
        /// </summary>
        public static AssetActionResult Success(string message, IList<object> outputs = null)
        {
            return new AssetActionResult { Status = message, Outputs = outputs, Succeeded = true };
        }

        /// <summary>
        /// Creates a failed result whose <paramref name="message"/> describes the error.
        /// </summary>
        public static AssetActionResult Failure(string message)
        {
            return new AssetActionResult { Status = message, Succeeded = false };
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
