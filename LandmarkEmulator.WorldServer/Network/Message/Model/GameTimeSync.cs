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
        /// <summary>
        /// Stops the client's clock while set: the handler FUN_1431dace0 copies it to ClientGameTimeManager +0xc0, and the
        /// time manager's update (FUN_143d934e0, FUN_143d92f90) does not advance the time of day while it is set [P]. The
        /// server never stops time (false).
        /// </summary>
        public bool Paused { get; set; }

        public void Read(GamePacketReader reader)
        {
            Time = reader.ReadDouble();
            CycleSpeed = reader.ReadSingle();
            Paused = reader.ReadBool();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Time);
            writer.Write(CycleSpeed);
            writer.Write(Paused);
        }
    }
}
