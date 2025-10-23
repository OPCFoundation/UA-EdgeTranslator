// MatterDotNet Copyright (C) 2025
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or any later version.
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Affero General Public License for more details.
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Buffers.Binary;
using System.Text;

namespace MatterDotNet.Protocol.Payloads
{
    /// <summary>
    /// Write payloads to a memory region
    /// </summary>
    public class PayloadWriter
    {
        private readonly Memory<byte> data;
        private int pos;

        /// <summary>
        /// Write payloads to a memory region
        /// </summary>
        /// <param name="data"></param>
        /// <param name="pos"></param>
        public PayloadWriter(Memory<byte> data, int pos = 0)
        {
            this.data = data;
            this.pos = pos;
        }

        /// <summary>
        /// Write payloads to a memory region
        /// </summary>
        /// <param name="capacity"></param>
        public PayloadWriter(int capacity)
        {
            data = new byte[capacity];
            pos = 0;
        }

        /// <summary>
        /// Write a byte
        /// </summary>
        /// <param name="value"></param>
        public void Write(byte value)
        {
            data.Span[pos++] = value;
        }

        /// <summary>
        /// Write a signed byte
        /// </summary>
        /// <param name="value"></param>
        public void Write(sbyte value)
        {
            data.Span[pos++] = (byte)value;
        }

        /// <summary>
        /// Write a byte array
        /// </summary>
        /// <param name="bytes"></param>
        public void Write(byte[] bytes)
        {
            bytes.CopyTo(data.Slice(pos).Span);
            pos += bytes.Length;
        }

        /// <summary>
        /// Write a Span of bytes
        /// </summary>
        /// <param name="bytes"></param>
        public void Write(ReadOnlySpan<byte> bytes)
        {
            bytes.CopyTo(data.Slice(pos).Span);
            pos += bytes.Length;
        }

        /// <summary>
        /// Write another PayloadWriter to this stream
        /// </summary>
        /// <param name="payload"></param>
        public void Write(PayloadWriter payload)
        {
            payload.CopyTo(data.Slice(pos));
            pos += payload.Length;
        }

        /// <summary>
        /// Write a region of Memory
        /// </summary>
        /// <param name="bytes"></param>
        public void Write(Memory<byte> bytes)
        {
            bytes.CopyTo(data.Slice(pos));
            pos += bytes.Length;
        }

        /// <summary>
        /// Write an int
        /// </summary>
        /// <param name="value"></param>
        public void Write(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.Span.Slice(pos, 4), value);
            pos += 4;
        }

        /// <summary>
        /// Write an unsigned int
        /// </summary>
        /// <param name="value"></param>
        public void Write(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.Span.Slice(pos, 4), value);
            pos += 4;
        }

        /// <summary>
        /// Write a long
        /// </summary>
        /// <param name="value"></param>
        public void Write(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(data.Span.Slice(pos, 8), value);
            pos += 8;
        }

        /// <summary>
        /// Write an unsigned long
        /// </summary>
        /// <param name="value"></param>
        public void Write(ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(data.Span.Slice(pos, 8), value);
            pos += 8;
        }

        /// <summary>
        /// Write a short
        /// </summary>
        /// <param name="value"></param>
        public void Write(short value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.Span.Slice(pos, 2), value);
            pos += 2;
        }

        /// <summary>
        /// Write an unsigned short
        /// </summary>
        /// <param name="value"></param>
        public void Write(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.Span.Slice(pos, 2), value);
            pos += 2;
        }

        /// <summary>
        /// Write a float
        /// </summary>
        /// <param name="value"></param>
        public void Write(float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.Span.Slice(pos, 4), value);
            pos += 2;
        }

        /// <summary>
        /// Write a double
        /// </summary>
        /// <param name="value"></param>
        public void Write(double value)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(data.Span.Slice(pos, 8), value);
            pos += 2;
        }

        /// <summary>
        /// Write a string
        /// </summary>
        /// <param name="value"></param>
        public void Write(string value)
        {
            pos += Encoding.UTF8.GetBytes(value, data.Span.Slice(pos));
        }

        /// <summary>
        /// Seek forward the provided number of bytes
        /// </summary>
        /// <param name="offset"></param>
        public void Seek(int offset)
        {
            pos += offset;
        }

        /// <summary>
        /// Length of the payload
        /// </summary>
        public int Length { get { return pos; } }

        /// <summary>
        /// Get the written payload
        /// </summary>
        /// <returns></returns>
        public Memory<byte> GetPayload()
        {
            return data.Slice(0, pos);
        }

        private void CopyTo(Memory<byte> slice)
        {
            data.Slice(0, pos).CopyTo(slice);
        }
    }
}
