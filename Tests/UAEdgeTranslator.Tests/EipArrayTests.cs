namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using Xunit;

    /// <summary>
    /// The proposed array extension of the EtherNet/IP binding: eipv:dimensions and
    /// eipv:elementCount, the row-major element layout, and the WoT array schema.
    /// </summary>
    public sealed class EipArrayTests
    {
        [Fact]
        public void An_array_form_is_written_and_read_with_its_dimensions()
        {
            EIPForm form = new() { Href = "Levels", Type = EIPTypeString.REAL, Dimensions = [10] };

            JObject json = JObject.FromObject(form);

            Assert.Equal(new[] { 10 }, json["eipv:dimensions"].ToObject<int[]>());
            Assert.Null(json["eipv:elementCount"]);
            Assert.Null(json["IsArray"]);
            Assert.Null(json["EffectiveElementCount"]);

            EIPForm read = JsonConvert.DeserializeObject<EIPForm>(json.ToString());
            Assert.True(read.IsArray);
            Assert.Equal(10, read.EffectiveElementCount);
        }

        [Theory]
        [InlineData(new[] { 10 }, null, 10)]
        [InlineData(new[] { 4, 5 }, null, 20)]
        [InlineData(new[] { 2, 3, 4 }, null, 24)]
        [InlineData(new[] { 10 }, 4, 4)]
        public void The_element_count_defaults_to_every_declared_element(int[] dimensions, int? elementCount, int expected)
        {
            EIPForm form = new() { Type = EIPTypeString.DINT, Dimensions = dimensions, ElementCount = elementCount };

            Assert.Null(form.ValidateArray());
            Assert.Equal(expected, form.EffectiveElementCount);
        }

        [Fact]
        public void Invalid_array_forms_are_rejected()
        {
            Assert.NotNull(new EIPForm { Type = EIPTypeString.DINT, Dimensions = [1, 2, 3, 4] }.ValidateArray());
            Assert.NotNull(new EIPForm { Type = EIPTypeString.DINT, Dimensions = [0] }.ValidateArray());
            Assert.NotNull(new EIPForm { Type = EIPTypeString.BOOL, Dimensions = [32] }.ValidateArray());
            Assert.NotNull(new EIPForm { Type = EIPTypeString.STRING, Dimensions = [4] }.ValidateArray());
            Assert.NotNull(new EIPForm { Type = EIPTypeString.DINT, Dimensions = [4], StructureDefinition = new EIPStructureDefinition { TypeName = "UDT" } }.ValidateArray());
            Assert.NotNull(new EIPForm { Type = EIPTypeString.DINT, Dimensions = [10], ElementCount = 11 }.ValidateArray());
            Assert.NotNull(new EIPForm { Type = EIPTypeString.DINT, ElementCount = 2 }.ValidateArray());
            Assert.Null(new EIPForm { Type = EIPTypeString.DINT }.ValidateArray());
        }

        [Fact]
        public void Elements_are_decoded_back_to_back_from_the_offset()
        {
            byte[] buffer = new byte[16];
            BitConverter.GetBytes(7).CopyTo(buffer, 4);
            BitConverter.GetBytes(-8).CopyTo(buffer, 8);
            BitConverter.GetBytes(9).CopyTo(buffer, 12);

            Array values = RockwellAsset.DecodeArray(buffer, 4, EIPTypeString.DINT, 3);

            Assert.Equal(new[] { 7, -8, 9 }, Assert.IsType<int[]>(values));
            Assert.Throws<ArgumentOutOfRangeException>(() => RockwellAsset.DecodeArray(buffer, 4, EIPTypeString.DINT, 4));
        }

        [Fact]
        public void Scale_and_offset_apply_to_every_element()
        {
            byte[] buffer = new byte[4];
            BitConverter.GetBytes((short)100).CopyTo(buffer, 0);
            BitConverter.GetBytes((short)250).CopyTo(buffer, 2);
            EIPForm form = new() { Type = EIPTypeString.INT, Dimensions = [2], Scale = 0.1 };

            double[] values = Assert.IsType<double[]>(RockwellAsset.DecodeArray(buffer, 0, EIPTypeString.INT, 2, form));
            Assert.Equal(10.0, values[0], 6);
            Assert.Equal(25.0, values[1], 6);

            byte[] written = new byte[4];
            RockwellAsset.EncodeArray(written, 0, EIPTypeString.INT, 2, new[] { 10.0, 25.0 }, form);
            Assert.Equal(buffer, written);
        }

        [Fact]
        public void A_write_must_supply_every_element_and_keeps_the_other_bytes()
        {
            byte[] buffer = { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA };

            RockwellAsset.EncodeArray(buffer, 2, EIPTypeString.REAL, 2, new[] { 1.5f, -2.0f });

            Assert.Equal(0xAA, buffer[0]);
            Assert.Equal(0xAA, buffer[1]);
            Assert.Equal(1.5f, BitConverter.ToSingle(buffer, 2));
            Assert.Equal(-2.0f, BitConverter.ToSingle(buffer, 6));

            Assert.Throws<ArgumentException>(() => RockwellAsset.EncodeArray(buffer, 2, EIPTypeString.REAL, 2, new[] { 1.5f }));
            Assert.Throws<ArgumentException>(() => RockwellAsset.EncodeArray(buffer, 2, EIPTypeString.REAL, 2, null));
        }

        [Fact]
        public void An_array_property_has_an_items_schema_and_old_properties_are_unchanged()
        {
            Property array = new()
            {
                Type = TypeEnum.Array,
                Items = new ArrayItems { Type = TypeEnum.Number },
                MinItems = 10,
                MaxItems = 10
            };

            JObject json = JObject.FromObject(array);
            Assert.Equal("array", (string)json["type"]);
            Assert.Equal("number", (string)json["items"]["type"]);
            Assert.Equal(10, (int)json["maxItems"]);

            JObject scalar = JObject.FromObject(new Property { Type = TypeEnum.Number });
            Assert.Null(scalar["items"]);
            Assert.Null(scalar["minItems"]);
            Assert.Null(scalar["maxItems"]);
        }

        private static EIPStructureDefinition Point(int? size) => new()
        {
            TypeName = "Point",
            Size = size,
            Fields =
            [
                new EIPFieldDefinition { Name = "X", Type = EIPTypeString.DINT, ByteOffset = 0 },
                new EIPFieldDefinition { Name = "Ok", Type = EIPTypeString.BOOL, ByteOffset = 4, BitOffset = 0 }
            ]
        };

        [Fact]
        public void Structure_elements_are_decoded_at_the_padded_stride()
        {
            byte[] buffer = new byte[2 + 24];
            for (int i = 0; i < 3; i++)
            {
                BitConverter.GetBytes(100 + i).CopyTo(buffer, 2 + i * 8);
                buffer[2 + i * 8 + 4] = (byte)(i % 2);
            }

            var elements = RockwellAsset.DecodeStructureArray(buffer, 2, 3, Point(8));
            Assert.Equal(new object[] { 100, 101, 102 }, Array.ConvertAll(elements, e => e["X"]));
            Assert.Equal(new object[] { false, true, false }, Array.ConvertAll(elements, e => e["Ok"]));
            Assert.Null(new EIPForm { Dimensions = [3], StructureDefinition = Point(8) }.ValidateArray());
            Assert.Equal(8, (int)JObject.FromObject(Point(8))["eipv:size"]);
        }

        [Fact]
        public void Without_a_size_the_stride_is_inferred_from_the_tag()
        {
            Assert.Equal(8, RockwellAsset.StructureStride(Point(null), 24, 0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => RockwellAsset.StructureStride(Point(null), 25, 0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => RockwellAsset.DecodeStructureArray(new byte[16], 0, 3, Point(8)));
        }
    }
}
