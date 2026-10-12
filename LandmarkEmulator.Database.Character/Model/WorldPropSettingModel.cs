namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One setting of a placed prop, from its settings window (EditableProperties STRING, e.g.
    /// "prop_timer_player_set_duration" = 30): see Game.PropSettings.
    /// </summary>
    public class WorldPropSettingModel
    {
        public ulong PropId { get; set; }
        public string Key { get; set; } = "";
        public float Value { get; set; }
    }
}
