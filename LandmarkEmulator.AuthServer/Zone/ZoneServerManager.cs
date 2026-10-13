using LandmarkEmulator.Shared;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using System.Collections.Immutable;
using System.Linq;

namespace LandmarkEmulator.AuthServer.Zone
{
    public class ZoneServerManager : Singleton<ZoneServerManager>
    {
        public ImmutableList<ZoneServer> ZoneServers { get; private set; }

        public void Initialise()
        {
            // AuthServer.json WorldAddress, when set, replaces the stored host (see AuthServerConfiguration.WorldAddress).
            string address = ConfigurationManager<AuthServerConfiguration>.Instance.Config?.WorldAddress?.Trim();
            ZoneServers = DatabaseManager.Instance.AuthDatabase.GetZoneServers()
                .Select(s =>
                {
                    if (!string.IsNullOrEmpty(address))
                        s.Host = address;
                    return new ZoneServer(s);
                })
                .ToImmutableList();
        }
    }
}
