namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>InspectBase 0x91 (u8 sub): 3 RequestInspectPlayer {u64 character} (0.695), 2 StopInspect.</summary>
    [ZoneMessage(ZoneMessageOpcode.InspectBase)]
    public class InspectRequest : SubRequest8 { }
}
