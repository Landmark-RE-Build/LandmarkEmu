namespace LandmarkEmulator.Database.Auth.Model
{
    /// <summary>
    /// A global admin entry in <c>lmemu_auth.admin</c>: the same string form as WorldServer.json / .env
    /// (<c>account:Name</c>, <c>character:Name</c>, or a bare name). Managed by SQL only; OR'd with the .env bootstrap list.
    /// </summary>
    public class AdminModel
    {
        public ulong Id { get; set; }
        public string Entry { get; set; }
    }
}
