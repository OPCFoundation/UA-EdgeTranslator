namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
{
    class GlobalCounter
    {
        private static uint counter = 0;

        public static uint Counter => counter++;
    }
}
