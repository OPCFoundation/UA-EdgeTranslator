namespace Opc.Ua.Edge.Translator.Models
{
    class TagInfo
    {
        public uint Id { get; set; }

        public ushort Type { get; set; }

        public string Name { get; set; }

        public ushort Length { get; set; }

        public uint[] Dimensions { get; set; }
    }

    class UdtFieldInfo
    {
        public string Name { get; set; }

        public ushort Type { get; set; }

        public ushort Metadata { get; set; }

        public uint Offset { get; set; }
    }

    class UdtInfo
    {
        public uint Size { get; set; }

        public string Name { get; set; }

        public ushort Id { get; set; }

        public ushort NumFields { get; set; }

        public ushort Handle { get; set; }

        public UdtFieldInfo[] Fields { get; set; }
    }
}
