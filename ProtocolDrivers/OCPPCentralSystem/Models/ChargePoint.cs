/*
Copyright 2021 Microsoft Corporation
*/


namespace OCPPCentralSystem.Models
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    public class ChargePoint
    {
        public string ID { get; set; } = string.Empty;

        public ConcurrentDictionary<int, Connector> Connectors { get; set; } = new();
    }

    public class Connector
    {
        public Connector(int id)
        {
            ID = id;
        }

        public int ID { get; set; }

        public string Status { get; set; } = string.Empty;

        public List<MeterReading> MeterReadings { get; set; } = new();

        public ConcurrentDictionary<int, Transaction> CurrentTransactions { get; set; } = new();
    }

    public class MeterReading
    {
        public int MeterValue { get; set; } = -1;

        public string MeterValueUnit { get; set; } = "Wh";

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class Transaction
    {
        public Transaction(int id)
        {
            ID = id;
        }

        public int ID { get; set; }

        public string BadgeID { get; set; } = string.Empty;

        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        public DateTime StopTime { get; set; } = DateTime.MinValue;

        public int MeterValueStart { get; set; } = -1;

        public int MeterValueFinish { get; set; } = -1;
    }
}
