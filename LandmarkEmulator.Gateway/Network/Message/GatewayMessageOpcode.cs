namespace LandmarkEmulator.Gateway.Network.Message
{
    /// <summary>
    /// External gateway packets: the low five bits of the first byte (Gateway::ExternalGatewayApi, receive switch
    /// 0.695 FUN_143a4bd20; trace/architecture/gateway-service.md §1.1).
    /// </summary>
    public enum GatewayMessageOpcode
    {
        LoginRequest                       = 0x01,
        LoginReply                         = 0x02,
        /// <summary>C→S, no body. The client's sender (FUN_143a4bb50) has no caller found yet (L71).</summary>
        Logout                             = 0x03,
        /// <summary>S→C ForcedLogout {StringFixed&lt;64&gt; reason}; the client's handler does nothing until the connection closes.</summary>
        ForceDisconnect                    = 0x04,
        TunnelPacketToExternalConnection   = 0x05,
        TunnelPacketFromExternalConnection = 0x06,
        /// <summary>S→C, channel in the first byte, bool isRoutable, bool (L70); the client only logs it.</summary>
        ChannelIsRoutable                  = 0x07,
        /// <summary>S→C, no body; the client only logs it.</summary>
        ConnectionIsNotRoutable            = 0x08,
        /// <summary>C→S: a slash command for the gateway itself (CommandList source 3): a ClientCommandPacket with its
        /// zone header (1E 00 43 00, string, u32 n × string); FUN_14337d580.</summary>
        GatewayCommand                     = 0x09
    }
}
