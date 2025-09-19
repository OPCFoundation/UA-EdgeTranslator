namespace Matter.Core
{
    using System;
    using System.IO;

    class MatterMessageWriter
    {
        private MemoryStream _stream;

        public MatterMessageWriter()
        {
            _stream = new MemoryStream();
        }

        internal void Write(byte @byte)
        {
            _stream.WriteByte(@byte);
        }

        internal void Write(byte[] bytes)
        {
            _stream.Write(bytes);
        }

        internal void Write(ushort value)
        {
            _stream.Write(BitConverter.GetBytes(value));
        }

        internal void Write(ulong value)
        {
            _stream.Write(BitConverter.GetBytes(value));
        }

        internal void Write(uint value)
        {
            _stream.Write(BitConverter.GetBytes(value));
        }

        internal byte[] GetBytes()
        {
            var bytes = _stream.ToArray();
            return bytes;
        }
    }
}
