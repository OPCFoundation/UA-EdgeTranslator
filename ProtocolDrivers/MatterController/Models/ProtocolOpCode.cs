namespace Matter.Core
{
    using System;

    [Flags]
    public enum ProtocolOpCode : byte
    {
        StatusResponse = 0x01,
        ReadRequest = 0x02,
        SubscribeRequest = 0x03,
        SubscribeResponse = 0x04,
        ReportData = 0x05,
        WriteRequest = 0x06,
        WriteResponse = 0x07,
        InvokeRequest = 0x08,
        InvokeResponse = 0x09,
        TimedRequest = 0x0A,
        Acknowledgement = 0x10,
        PBKDFParamRequest = 0x20,
        PBKDFParamResponse = 0x21,
        PASEPake1 = 0x22,
        PASEPake2 = 0x23,
        PASEPake3 = 0x24,
        CASESigma1 = 0x30,
        CASESigma2 = 0x31,
        CASESigma3 = 0x32,
        CASESigma2_Resume = 0x33,
        StatusReport = 0x40,
        CheckInMessage = 0x50
    }
}
