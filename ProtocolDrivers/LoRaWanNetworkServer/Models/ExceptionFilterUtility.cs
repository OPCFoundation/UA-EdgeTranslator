// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan
{
    using System;

    public static class ExceptionFilterUtility
    {
        public static bool True(params Action[] actions)
        {
            ArgumentNullException.ThrowIfNull(actions);

            foreach (var a in actions)
                a();

            return true;
        }

        public static bool False(params Action[] actions)
        {
            ArgumentNullException.ThrowIfNull(actions);

            foreach (var a in actions)
                a();

            return false;
        }
    }
}
