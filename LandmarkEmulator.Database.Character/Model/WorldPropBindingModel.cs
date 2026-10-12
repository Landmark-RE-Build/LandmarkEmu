namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A story-tool Link between two placed props: when <see cref="SourceId"/> raises <see cref="EventId"/>, the
    /// server runs <see cref="ActionId"/> on <see cref="TargetId"/> (ObjectScripting AddBinding). Event and action ids
    /// are the text ids of their client strings ("When this torch is lit...", "...Light this torch.").
    /// </summary>
    public class WorldPropBindingModel
    {
        public ulong Id { get; set; }
        public ulong SourceId { get; set; }
        public uint EventId { get; set; }
        public ulong TargetId { get; set; }
        public uint ActionId { get; set; }
    }
}
