// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LoRaWANContainer.LoRaWan.NetworkServer.Models;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    public interface IFunctionBundlerExecutionItem
    {
        bool RequiresExecution(FunctionBundlerExecutionContext context);

        void Prepare(FunctionBundlerExecutionContext context, FunctionBundlerRequest request);

        void ProcessResult(FunctionBundlerExecutionContext context, FunctionBundlerResult result);
    }
}
