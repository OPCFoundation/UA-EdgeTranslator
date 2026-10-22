namespace UAEdgeTranslator.Tests
{
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Xunit;

    public class EipConformanceTests
    {
        private static EIPForm Form(EIPTypeString? type = EIPTypeString.DINT, params Op[] ops) => new()
        {
            Href = "Counter",
            Op = ops.Length == 0 ? [Op.Readproperty] : ops,
            Type = type
        };

        [Fact]
        public void Controller_defaults_to_ControlLogix_and_rejects_unknown_families()
        {
            Assert.Equal(EIPController.ControlLogix, EIPControllers.FromThingDescription(new ThingDescription()));

            ThingDescription td = new() { AdditionalData = new Dictionary<string, object> { ["eipv:controller"] = "SLC500" } };
            Assert.Equal(EIPController.SLC500, EIPControllers.FromThingDescription(td));

            td.AdditionalData["eipv:controller"] = "S7-1500";
            Assert.Throws<System.ArgumentException>(() => EIPControllers.FromThingDescription(td));
        }

        [Fact]
        public void Zero_scale_and_unmapped_operations_are_rejected()
        {
            Assert.Null(Form().Validate(EIPController.ControlLogix));

            EIPForm zero = Form();
            zero.Scale = 0;
            Assert.NotNull(zero.Validate(EIPController.ControlLogix));

            Assert.NotNull(Form(EIPTypeString.DINT, Op.Invokeaction).Validate(EIPController.ControlLogix));
            Assert.NotNull(Form(EIPTypeString.DINT, Op.Subscribeevent).Validate(EIPController.ControlLogix));
        }

        [Fact]
        public void Services_match_operations_and_controller_family()
        {
            EIPForm read = Form(EIPTypeString.DINT, Op.Readproperty, Op.Observeproperty);
            read.Service = EIPServices.ReadTagFragmented;
            Assert.Null(read.Validate(EIPController.ControlLogix));
            Assert.NotNull(read.Validate(EIPController.MicroLogix));

            EIPForm mixed = Form(EIPTypeString.DINT, Op.Readproperty, Op.Writeproperty);
            mixed.Service = EIPServices.WriteTag;
            Assert.NotNull(mixed.Validate(EIPController.ControlLogix));

            EIPForm pccc = Form(EIPTypeString.INT, Op.Readproperty, Op.Writeproperty);
            pccc.Service = EIPServices.ExecutePCCC;
            Assert.Null(pccc.Validate(EIPController.SLC500));
            Assert.NotNull(pccc.Validate(EIPController.CompactLogix));

            EIPForm unknown = Form();
            unknown.Service = "GetAttributeSingle";
            Assert.NotNull(unknown.Validate(EIPController.ControlLogix));
        }

        [Fact]
        public void Pccc_families_use_typed_file_addresses_only()
        {
            EIPForm array = Form();
            array.Dimensions = [10];
            Assert.NotNull(array.Validate(EIPController.PLC5));

            EIPForm offset = Form();
            offset.Href = "./N7:1?offset=2";
            Assert.NotNull(offset.Validate(EIPController.MicroLogix));

            Assert.NotNull(Form(null).Validate(EIPController.MicroLogix));
            Assert.Null(Form(null).Validate(EIPController.ControlLogix));
        }

        [Fact]
        public void Structure_definitions_follow_the_field_rules()
        {
            EIPForm form = Form(null);
            form.StructureDefinition = new EIPStructureDefinition
            {
                TypeName = "Motor",
                Fields = [new EIPFieldDefinition { Name = "Running", Type = EIPTypeString.BOOL, ByteOffset = 0 }]
            };
            Assert.NotNull(form.Validate(EIPController.ControlLogix));

            form.StructureDefinition.Fields[0].BitOffset = 8;
            Assert.NotNull(form.Validate(EIPController.ControlLogix));

            form.StructureDefinition.Fields[0].BitOffset = 0;
            Assert.Null(form.Validate(EIPController.ControlLogix));

            form.Dimensions = [4];
            form.Scale = 2;
            Assert.NotNull(form.Validate(EIPController.ControlLogix));
        }

        [Fact]
        public void Byte_size_is_read_and_legacy_size_is_still_accepted()
        {
            Assert.Equal(12, JsonConvert.DeserializeObject<EIPStructureDefinition>("{\"eipv:typeName\":\"T\",\"eipv:byteSize\":12,\"eipv:fields\":[]}").Size);
            Assert.Equal(16, JsonConvert.DeserializeObject<EIPStructureDefinition>("{\"eipv:typeName\":\"T\",\"eipv:size\":16,\"eipv:fields\":[]}").Size);
        }

        [Fact]
        public void Array_members_are_decoded_as_flat_arrays()
        {
            EIPStructureDefinition definition = new()
            {
                TypeName = "Recipe",
                Fields =
                [
                    new EIPFieldDefinition { Name = "Count", Type = EIPTypeString.DINT, ByteOffset = 0 },
                    new EIPFieldDefinition { Name = "Steps", Type = EIPTypeString.INT, ByteOffset = 4, Dimensions = [2, 2] }
                ]
            };
            Assert.Null(definition.Validate());

            byte[] buffer = new byte[12];
            System.BitConverter.GetBytes(7).CopyTo(buffer, 0);
            for (short i = 0; i < 4; i++)
            {
                System.BitConverter.GetBytes((short)(10 + i)).CopyTo(buffer, 4 + i * 2);
            }

            var fields = RockwellAsset.DecodeUdtFields(buffer, 0, definition);
            Assert.Equal(7, fields["Count"]);
            Assert.Equal(new short[] { 10, 11, 12, 13 }, (short[])fields["Steps"]);
        }

        [Fact]
        public void Written_values_round_to_the_nearest_representable_value()
        {
            EIPForm form = new() { Type = EIPTypeString.INT, Scale = 0.1 };
            Assert.Equal(32767d, form.ToControllerValue(100000.0));
            Assert.Equal(-32768d, form.ToControllerValue(-100000.0));
            Assert.Equal(215d, form.ToControllerValue(21.46));
        }

        [Theory]
        [InlineData("StatusWord.3", EIPController.ControlLogix, true)]
        [InlineData("Mixer.Status.Running", EIPController.ControlLogix, false)]
        [InlineData("B3:0/3", EIPController.SLC500, true)]
        [InlineData("N7:1", EIPController.MicroLogix, false)]
        public void Bit_references_are_recognized(string tagPath, EIPController controller, bool isBit)
        {
            Assert.Equal(isBit, RockwellAsset.IsBitPath(tagPath, controller));
        }
    }
}
