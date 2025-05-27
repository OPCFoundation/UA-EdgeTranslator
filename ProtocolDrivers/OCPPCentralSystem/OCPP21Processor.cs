namespace OCPPCentralSystem
{
    using System;
    using System.Threading.Tasks;

    public class OCPP21Processor
    {
        static private int _transactionNumber = 0;

        static public Task<string> ProcessRequestPayloadAsync(string uniqueId, string action, string payload)
        {
            string responsePayload = string.Empty;

            try
            {
                return Task.FromResult($"Processed {action} for {uniqueId} with payload {payload} and transaction {_transactionNumber}.");
            }
            catch (Exception ex)
            {
                return Task.FromResult("Error processing request: " + ex.Message);
            }
        }
    }
}
