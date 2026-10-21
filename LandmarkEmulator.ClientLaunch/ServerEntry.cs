namespace LandmarkEmulator.ClientLaunch
{
    /// <summary>
    /// One server the player added: just its address. Everything else (name, login port, web pages, patches) comes from
    /// the server's /info when needed, so a server owner's changes reach players by themselves.
    /// </summary>
    public sealed class ServerEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        /// <summary>Host name or IP address.</summary>
        public string Address { get; set; } = "127.0.0.1";
        /// <summary>The account service (auth server web API); typed as "address:port" when it isn't 5000.</summary>
        public int ApiPort { get; set; } = 5000;
        public string Account { get; set; } = "";
        /// <summary>
        /// The SRP key (PBKDF2 of the password with this account's salt) when "remember me" is on: it works only for this
        /// account on this server; the password itself is never stored.
        /// </summary>
        public string SavedKey { get; set; } = "";
        public string SavedSalt { get; set; } = "";
        /// <summary>"host" or "host:port" as the player typed it.</summary>
        public string Display => ApiPort == 5000 ? Address : $"{Address}:{ApiPort}";
    }
}
