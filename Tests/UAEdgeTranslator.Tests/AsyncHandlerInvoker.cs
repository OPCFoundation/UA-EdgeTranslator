namespace Opc.Ua.Edge.Translator.Tests
{
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Opc.Ua;

    /// <summary>
    /// Invokes the private async OPC UA method handlers (OnCallMethod2Async and
    /// the typed Close hook) by reflection and awaits their ValueTask result.
    /// </summary>
    internal static class AsyncHandlerInvoker
    {
        /// <summary>
        /// Calls a handler with the OnCallMethod2Async signature: (context, method,
        /// objectId, inputArguments, outputArguments, cancellationToken).
        /// </summary>
        public static ServiceResult CallMethod(object target, string name, MethodState method, ArrayOf<Variant> inputs, List<Variant> outputs)
            => Invoke<ServiceResult>(target, name, null, method, NodeId.Null, inputs, outputs, CancellationToken.None);

        /// <summary>
        /// Invokes any async handler by name and blocks on its ValueTask result.
        /// </summary>
        public static T Invoke<T>(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Xunit.Assert.NotNull(method);

            object result;
            try
            {
                result = method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            return ((ValueTask<T>)result).AsTask().GetAwaiter().GetResult();
        }
    }
}
