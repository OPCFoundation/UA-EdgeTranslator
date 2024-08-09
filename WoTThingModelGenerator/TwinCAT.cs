namespace WoTThingModelGenerator.TwinCAT
{
    using System.Collections.Generic;
    using System.Xml.Serialization;

#nullable disable

    [XmlRoot(ElementName = "TcModuleClass")]
    public class TwinCAT
    {
        [XmlElement(ElementName = "DataTypes")]
        public DataTypes DataTypes { get; set; }

        [XmlElement(ElementName = "Modules")]
        public Modules Modules { get; set; }

        [XmlAttribute(AttributeName = "xsi")]
        public string Xsi { get; set; }

        [XmlAttribute(AttributeName = "noNamespaceSchemaLocation")]
        public string NoNamespaceSchemaLocation { get; set; }

        [XmlAttribute(AttributeName = "Hash")]
        public string Hash { get; set; }

        [XmlAttribute(AttributeName = "GeneratedBy")]
        public string GeneratedBy { get; set; }

        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "Name")]
    public class Name
    {

        [XmlAttribute(AttributeName = "GUID")]
        public string GUID { get; set; }

        [XmlAttribute(AttributeName = "TcBaseType")]
        public bool TcBaseType { get; set; }

        [XmlText]
        public string Text { get; set; }

        [XmlAttribute(AttributeName = "Namespace")]
        public string Namespace { get; set; }
    }

    [XmlRoot(ElementName = "Type")]
    public class Type
    {

        [XmlAttribute(AttributeName = "GUID")]
        public string GUID { get; set; }

        [XmlText]
        public string Text { get; set; }

        [XmlAttribute(AttributeName = "Namespace")]
        public string Namespace { get; set; }

        [XmlAttribute(AttributeName = "PointerTo")]
        public int PointerTo { get; set; }
    }

    [XmlRoot(ElementName = "SubItem")]
    public class SubItem
    {

        [XmlElement(ElementName = "BitSize")]
        public BitSize BitSize { get; set; }

        [XmlElement(ElementName = "BitOffs")]
        public int BitOffs { get; set; }

        [XmlElement(ElementName = "Properties")]
        public Properties Properties { get; set; }

        [XmlElement(ElementName = "Name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "Type")]
        public Type Type { get; set; }

        [XmlElement(ElementName = "Value")]
        public int Value { get; set; }

        [XmlElement(ElementName = "String")]
        public string String { get; set; }
    }

    [XmlRoot(ElementName = "DataType")]
    public class DataType
    {

        [XmlElement(ElementName = "Name")]
        public Name Name { get; set; }

        [XmlElement(ElementName = "BitSize")]
        public int BitSize { get; set; }

        [XmlElement(ElementName = "SubItem")]
        public List<SubItem> SubItem { get; set; }

        [XmlElement(ElementName = "BaseType")]
        public BaseType BaseType { get; set; }

        [XmlElement(ElementName = "EnumInfo")]
        public List<EnumInfo> EnumInfo { get; set; }

        [XmlElement(ElementName = "Hides")]
        public Hides Hides { get; set; }

        [XmlElement(ElementName = "Properties")]
        public Properties Properties { get; set; }
    }

    [XmlRoot(ElementName = "BaseType")]
    public class BaseType
    {

        [XmlAttribute(AttributeName = "GUID")]
        public string GUID { get; set; }

        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "EnumInfo")]
    public class EnumInfo
    {

        [XmlElement(ElementName = "Text")]
        public string Text { get; set; }

        [XmlElement(ElementName = "Enum")]
        public int Enum { get; set; }
    }

    [XmlRoot(ElementName = "BitSize")]
    public class BitSize
    {

        [XmlAttribute(AttributeName = "X64")]
        public int X64 { get; set; }

        [XmlText]
        public int Text { get; set; }
    }

    [XmlRoot(ElementName = "Property")]
    public class Property
    {

        [XmlElement(ElementName = "Name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "Value")]
        public string Value { get; set; }
    }

    [XmlRoot(ElementName = "Properties")]
    public class Properties
    {

        [XmlElement(ElementName = "Property")]
        public List<Property> Property { get; set; }
    }

    [XmlRoot(ElementName = "Hide")]
    public class Hide
    {

        [XmlAttribute(AttributeName = "GUID")]
        public string GUID { get; set; }
    }

    [XmlRoot(ElementName = "Hides")]
    public class Hides
    {

        [XmlElement(ElementName = "Hide")]
        public List<Hide> Hide { get; set; }
    }

    [XmlRoot(ElementName = "DataTypes")]
    public class DataTypes
    {

        [XmlElement(ElementName = "DataType")]
        public List<DataType> DataType { get; set; }
    }

    [XmlRoot(ElementName = "CLSID")]
    public class CLSID
    {

        [XmlAttribute(AttributeName = "ClassFactory")]
        public string ClassFactory { get; set; }

        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "Id")]
    public class Id
    {

        [XmlAttribute(AttributeName = "NeedCalleeCall")]
        public bool NeedCalleeCall { get; set; }

        [XmlAttribute(AttributeName = "OTCID")]
        public string OTCID { get; set; }

        [XmlText]
        public int Text { get; set; }
    }

    [XmlRoot(ElementName = "ManualConfig")]
    public class ManualConfig
    {

        [XmlElement(ElementName = "OTCID")]
        public string OTCID { get; set; }
    }

    [XmlRoot(ElementName = "Context")]
    public class Context
    {

        [XmlElement(ElementName = "Id")]
        public Id Id { get; set; }

        [XmlElement(ElementName = "Name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "ManualConfig")]
        public ManualConfig ManualConfig { get; set; }
    }

    [XmlRoot(ElementName = "Contexts")]
    public class Contexts
    {

        [XmlElement(ElementName = "Context")]
        public Context Context { get; set; }
    }

    [XmlRoot(ElementName = "AreaNo")]
    public class AreaNo
    {

        [XmlAttribute(AttributeName = "AreaType")]
        public string AreaType { get; set; }

        [XmlAttribute(AttributeName = "CreateSymbols")]
        public bool CreateSymbols { get; set; }

        [XmlText]
        public int Text { get; set; }
    }

    [XmlRoot(ElementName = "Default")]
    public class Default
    {

        [XmlElement(ElementName = "SubItem")]
        public List<SubItem> SubItem { get; set; }
    }

    [XmlRoot(ElementName = "Symbol")]
    public class Symbol
    {

        [XmlElement(ElementName = "Name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "BitSize")]
        public int BitSize { get; set; }

        [XmlElement(ElementName = "BaseType")]
        public BaseType BaseType { get; set; }

        [XmlElement(ElementName = "Default")]
        public Default Default { get; set; }

        [XmlElement(ElementName = "Properties")]
        public Properties Properties { get; set; }

        [XmlElement(ElementName = "BitOffs")]
        public int BitOffs { get; set; }

        [XmlElement(ElementName = "ArrayInfo")]
        public ArrayInfo ArrayInfo { get; set; }
    }

    [XmlRoot(ElementName = "ArrayInfo")]
    public class ArrayInfo
    {

        [XmlElement(ElementName = "LBound")]
        public int LBound { get; set; }

        [XmlElement(ElementName = "Elements")]
        public int Elements { get; set; }
    }

    [XmlRoot(ElementName = "DataArea")]
    public class DataArea
    {

        [XmlElement(ElementName = "AreaNo")]
        public AreaNo AreaNo { get; set; }

        [XmlElement(ElementName = "Name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "ContextId")]
        public int ContextId { get; set; }

        [XmlElement(ElementName = "ByteSize")]
        public int ByteSize { get; set; }

        [XmlElement(ElementName = "Symbol")]
        public List<Symbol> Symbol { get; set; }
    }

    [XmlRoot(ElementName = "DataAreas")]
    public class DataAreas
    {

        [XmlElement(ElementName = "DataArea")]
        public DataArea DataArea { get; set; }
    }

    [XmlRoot(ElementName = "Module")]
    public class Module
    {

        [XmlElement(ElementName = "Name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "CLSID")]
        public CLSID CLSID { get; set; }

        [XmlElement(ElementName = "Licenses")]
        public object Licenses { get; set; }

        [XmlElement(ElementName = "Contexts")]
        public Contexts Contexts { get; set; }

        [XmlElement(ElementName = "Parameters")]
        public object Parameters { get; set; }

        [XmlElement(ElementName = "DataAreas")]
        public DataAreas DataAreas { get; set; }

        [XmlElement(ElementName = "Deployment")]
        public object Deployment { get; set; }

        [XmlElement(ElementName = "EventClasses")]
        public object EventClasses { get; set; }

        [XmlElement(ElementName = "Properties")]
        public Properties Properties { get; set; }

        [XmlAttribute(AttributeName = "GUID")]
        public string GUID { get; set; }

        [XmlAttribute(AttributeName = "TcSmClass")]
        public string TcSmClass { get; set; }

        [XmlAttribute(AttributeName = "TargetPlatform")]
        public string TargetPlatform { get; set; }

        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "Modules")]
    public class Modules
    {

        [XmlElement(ElementName = "Module")]
        public Module Module { get; set; }
    }
}
