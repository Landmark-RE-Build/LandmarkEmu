using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// GameTimeSync 0x37 {timestamp, f32 cycle speed, bool}. The client sets its time of day from the timestamp's UTC
    /// hours/minutes/seconds and advances it at the cycle speed (Game.WorldClock). The client also sends it (C→S) to ask.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.GameTimeSync)]
    public class GameTimeSync : IWritable, IReadable
    {
        public double Time { get; set; }
        public float CycleSpeed { get; set; }
        public bool Unknown0 { get; set; }

        public void Read(GamePacketReader reader)
        {
            Time = reader.ReadDouble();
            CycleSpeed = reader.ReadSingle();
            Unknown0 = reader.ReadBool();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Time);
            writer.Write(CycleSpeed);
            writer.Write(Unknown0);
        }
    }
}
