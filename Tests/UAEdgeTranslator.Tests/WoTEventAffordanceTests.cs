namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using System.Collections.Generic;
    using Xunit;

    /// <summary>
    /// Guards the boundary between the two ways a Thing Description's events are
    /// consumed, which must not overlap:
    /// <list type="bullet">
    /// <item>push-based alarm streams (OPC A&amp;E), surfaced through
    /// <c>IEventingAsset</c> and an Alarms folder;</item>
    /// <item>pull-based value affordances (the W3C WoT LoRaWAN binding, which
    /// models sensor readings as events rather than properties), surfaced as
    /// variables.</item>
    /// </list>
    /// </summary>
    public class WoTEventAffordanceTests
    {
        [Fact]
        public void An_opc_ae_alarm_event_declares_no_data_schema_type()
        {
            // OPC A&E events are a push stream delivered through IEventingAsset
            // and an Alarms folder; the driver throws from CreateTag because
            // there is nothing to poll. Such an event carries no scalar data
            // type, which is what distinguishes it from a value affordance.
            const string alarmEvent = /*lang=json,strict*/ """
            {
              "alarms": {
                "data": { "type": "object" },
                "forms": [ { "href": "opc.ae://plant/alarms", "op": "subscribeevent" } ]
              }
            }
            """;

            Dictionary<string, TDEvent> events =
                JsonConvert.DeserializeObject<Dictionary<string, TDEvent>>(alarmEvent);

            Assert.NotNull(events["alarms"].Forms);
            Assert.Single(events["alarms"].Forms);
        }

        [Fact]
        public void A_lorawan_event_carries_the_data_schema_the_variable_path_needs()
        {
            // The LoRaWAN binding models a sensor reading as an event, and the
            // node manager projects it onto a variable. The type therefore has to
            // be readable from the event's data schema, not from a property.
            const string sensorEvent = /*lang=json,strict*/ """
            {
              "temperature": {
                "data": { "type": "number", "uav:mapToNodeId": "nsu=http://example.org/;i=1234" },
                "forms": [ { "href": "uplink", "op": "subscribeevent" } ]
              }
            }
            """;

            Dictionary<string, TDEvent> events =
                JsonConvert.DeserializeObject<Dictionary<string, TDEvent>>(sensorEvent);

            Assert.NotNull(events["temperature"].Data);
            Assert.Single(events["temperature"].Forms);
        }
    }
}
