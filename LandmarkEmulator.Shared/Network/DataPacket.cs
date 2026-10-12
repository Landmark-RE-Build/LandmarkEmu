namespace LandmarkEmulator.Shared.Network
{
    public class DataPacket
    {
        public byte[] Data { get; }
        public bool IsFragment { get; }

        /// <summary>UTC ticks when this sequence first hit UDP (0 if not yet sent).</summary>
        public long FirstSendTicks { get; private set; }

        /// <summary>UTC ticks of the most recent UDP send/resend.</summary>
        public long LastSendTicks { get; private set; }

        /// <summary>How many times this sequence has been written to UDP (1 = first-try).</summary>
        public int SendCount { get; private set; }

        public DataPacket(byte[] data, bool isFragment)
        {
            Data = data;
            IsFragment = isFragment;
        }

        public void MarkSent(long utcTicks)
        {
            if (FirstSendTicks == 0)
                FirstSendTicks = utcTicks;
            LastSendTicks = utcTicks;
            SendCount++;
        }
    }
}
