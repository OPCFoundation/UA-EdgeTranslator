// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//------------------------------------------------------------------------------
// This code was generated by a tool.
// Changes to this file will be lost if the code is re-generated.
//------------------------------------------------------------------------------

#nullable enable

namespace LoRaWan
{
    using System;

    readonly partial record struct AppKey
    {
        public const int Size = Data128.Size;

        readonly Data128 value;

        AppKey(Data128 value) => this.value = value;

        public override string ToString() => this.value.ToString();

        public static AppKey Parse(ReadOnlySpan<char> input) =>
            TryParse(input, out var result) ? result : throw new FormatException();

        public static bool TryParse(ReadOnlySpan<char> input, out AppKey result)
        {
            if (Data128.TryParse(input) is (true, var raw))
            {
                result = new AppKey(raw);
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        public static AppKey Read(ReadOnlySpan<byte> buffer) =>
            new(Data128.Read(buffer));

        public static AppKey Read(ref ReadOnlySpan<byte> buffer) =>
            new(Data128.Read(ref buffer));

        public Span<byte> Write(Span<byte> buffer) => this.value.Write(buffer);
    }

    readonly partial record struct AppSessionKey
    {
        public const int Size = Data128.Size;

        readonly Data128 value;

        AppSessionKey(Data128 value) => this.value = value;

        public override string ToString() => this.value.ToString();

        public static AppSessionKey Parse(ReadOnlySpan<char> input) =>
            TryParse(input, out var result) ? result : throw new FormatException();

        public static bool TryParse(ReadOnlySpan<char> input, out AppSessionKey result)
        {
            if (Data128.TryParse(input) is (true, var raw))
            {
                result = new AppSessionKey(raw);
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        public static AppSessionKey Read(ReadOnlySpan<byte> buffer) =>
            new(Data128.Read(buffer));

        public static AppSessionKey Read(ref ReadOnlySpan<byte> buffer) =>
            new(Data128.Read(ref buffer));

        public Span<byte> Write(Span<byte> buffer) => this.value.Write(buffer);
    }

    readonly partial record struct NetworkSessionKey
    {
        public const int Size = Data128.Size;

        readonly Data128 value;

        NetworkSessionKey(Data128 value) => this.value = value;

        public override string ToString() => this.value.ToString();

        public static NetworkSessionKey Parse(ReadOnlySpan<char> input) =>
            TryParse(input, out var result) ? result : throw new FormatException();

        public static bool TryParse(ReadOnlySpan<char> input, out NetworkSessionKey result)
        {
            if (Data128.TryParse(input) is (true, var raw))
            {
                result = new NetworkSessionKey(raw);
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        public static NetworkSessionKey Read(ReadOnlySpan<byte> buffer) =>
            new(Data128.Read(buffer));

        public static NetworkSessionKey Read(ref ReadOnlySpan<byte> buffer) =>
            new(Data128.Read(ref buffer));

        public Span<byte> Write(Span<byte> buffer) => this.value.Write(buffer);
    }
}
