using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Internal
{
    /// <summary>
    /// ServiceStatus (ours; TASKS B.7, admin-pages.md: "/status from the other containers"): a service tells the zones it
    /// is linked to how it is doing, every <see cref="Interval"/>, for the world's admin monitor page (/admin/monitor). A
    /// <see cref="ServicePacket"/> of family <see cref="Family"/> (no client family uses 0xFFFF) and sub <see cref="Sub"/>:
    /// string service name, u32 n × {string key, string value}. Rows are plain text: the page shows them as they come.
    /// </summary>
    public static class ServiceStatus
    {
        public const ushort Family = 0xFFFF;
        public const uint Sub = 0x10F01;
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

        public static ServicePacket Build(string service, IEnumerable<(string Key, string Value)> rows)
        {
            var w = new InternalWriter();
            var list = new List<(string, string)>(rows);
            w.Str(service ?? "").U32((uint)list.Count);
            foreach ((string key, string value) in list)
                w.Str(key ?? "").Str(value ?? "");
            return new ServicePacket { Family = Family, Sub = Sub, Body = w.ToArray() };
        }

        public static bool TryRead(ServicePacket packet, out string service, out List<(string Key, string Value)> rows)
        {
            service = null;
            rows = null;
            if (packet == null || packet.Family != Family || packet.Sub != Sub)
                return false;
            try
            {
                var r = new InternalReader(packet.Body);
                service = r.Str();
                rows = new List<(string, string)>();
                for (uint n = r.U32(); n > 0 && rows.Count < 256; n--)
                    rows.Add((r.Str(), r.Str()));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
