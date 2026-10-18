using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The dev commands' usage lines, checked before a command runs (<see cref="Run"/>; the client-code checks are in
    /// DevCommands.ClientCode.cs):
    /// <list type="bullet">
    /// <item>Every command has a usage line in <see cref="Usages"/> (syntax: <c>&lt;x&gt;</c> required, <c>[x]</c> optional,
    /// <c>a | b</c> alternatives). A command given fewer arguments than its usage needs (<see cref="RequiredArguments"/>)
    /// is answered "usage: …" and not run; an argument that doesn't parse is answered the same way
    /// (<see cref="IsBadArgument"/>). Until 2026-10-18 both threw inside the command and only "Dev command failed" with a
    /// stack trace reached the log.</item>
    /// </list>
    /// LandmarkEmulator.Tests DevCommandUsageTests checks that every <c>case</c> of the command switch has a usage line and
    /// that every command with required arguments answers its usage, without throwing, when it has none.
    /// </summary>
    public static partial class DevCommands
    {
        /// <summary>
        /// Every dev command's usage line, by command name. Keep in step with the switch in DevCommands.cs (the test
        /// above compares them) and with each case's leading comment (the wiki's command pages are made from those).
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> Usages = new Dictionary<string, string>
        {
            ["give"] = "give <item id> <count>",
            ["giveoverflow"] = "giveoverflow <item> <count> [minutes]",
            ["send"] = "send <hex bytes>",
            ["testcell"] = "testcell <level> <x> <y> <z> [height 0-65535] [extent] [variant] [origin]",
            ["timeofday"] = "timeofday <hour 0-24>",
            ["clock"] = "clock",
            ["ringscale"] = "ringscale <scale>",
            ["pinata"] = "pinata <definition> [kind] [a b c] [dz]",
            ["digmode"] = "digmode <n>",
            ["carvefwd"] = "carvefwd <ahead m> <right m> <radius dm>",
            ["rimmode"] = "rimmode <n>",
            ["carve"] = "carve <dx> <dz> <radius dm> [dy]",
            ["livecell"] = "livecell <level> <x> <y> <z> [height] [variant]",
            ["blockdmg"] = "blockdmg <level> <x> <y> <z> <firstBlock> <lastBlock> <health%>",
            ["blockcell"] = "blockcell <level> <x> <y> <z> <x0> <y0> <z0> <x1> <y1> <z1> [material] [layer] [prefix 0|1]",
            ["dig"] = "dig [dx dy dz]",
            ["tpto"] = "tpto <character>",
            ["tpz"] = "tpz <x> <y> <z>",
            ["tp"] = "tp <x> <y> <z> [nosnap]",
            ["tpo"] = "tpo <dx> <dy> <dz>",
            ["cave"] = "cave [zone]",
            ["cavetrip"] = "cavetrip <crystal|adventure> [client world id] [zone]",
            ["cavelands"] = "cavelands [zone]",
            ["outpostrotate"] = "outpostrotate [zone|here] [count]",
            ["objective"] = "objective <id> [amount]",
            ["studiotake"] = "studiotake <product id>",
            ["pioneerslab"] = "pioneerslab",
            ["achreset"] = "achreset <achievement id>",
            ["rezonecave"] = "rezonecave",
            ["guildpacket"] = "guildpacket <sub hex> [body hex]",
            ["sysmail"] = "sysmail [subject]",
            ["mailpacket"] = "mailpacket <sub hex> [body hex]",
            ["lua"] = "lua <Table:Function|Table.Function> [a b c]",
            ["spire"] = "spire",
            ["creatures"] = "creatures",
            ["treepools"] = "treepools [fell <n>|now]",
            ["pinatafor"] = "pinatafor <item> [item ...]",
            ["attackinfo"] = "attackinfo [ability id ...]",
            ["ambients"] = "ambients",
            ["unwield"] = "unwield [equipment slot]",
            ["weaponkeywords"] = "weaponkeywords [on|off]",
            ["movementeffects"] = "movementeffects [on|off]",
            ["meleetargeters"] = "meleetargeters [on|off]",
            ["blockvisible"] = "blockvisible [on|off]",
            ["propstream"] = "propstream [metres|off]",
            ["safeeject"] = "safeeject [clear|on|off]",
            ["blockstances"] = "blockstances",
            ["movement"] = "movement",
            ["charstats"] = "charstats [on|off]",
            ["fire"] = "fire [power]",
            ["projectile"] = "projectile <definition> [distance] [speed] [r]",
            ["itemslot"] = "itemslot <equipment slot id> [model text]",
            ["iteminfo"] = "iteminfo <item id> [item id ...]",
            ["hitnear"] = "hitnear [damage]",
            ["struck"] = "struck [damage] [count]",
            ["slay"] = "slay [radius]",
            ["clearcreatures"] = "clearcreatures [radius]",
            ["respawn"] = "respawn [type]",
            ["creature"] = "creature [kobold|runt|shaman|skeleton|slaug|ogre] [dx dz]",
            ["kobold"] = "kobold [dx dz]",
            ["npc"] = "npc <model> [dx dz]",
            ["pos"] = "pos",
            ["lastmove"] = "lastmove",
            ["hurt"] = "hurt [hp]",
            ["coins"] = "coins <+n|-n|=n>",
            ["take"] = "take <item> [count]",
            ["drop"] = "drop [metres]",
            ["tree"] = "tree [dx dz [plant]]",
            ["treefall"] = "treefall [u64 u32 u32 u32 u32 u32 f f f f]",
            ["equipped"] = "equipped",
            ["slotitems"] = "slotitems <equip slot id>",
            ["finditem"] = "finditem [text|#id]",
            ["shutdown"] = "shutdown <seconds> [message] | shutdown cancel",
            ["broadcast"] = "broadcast <text> | broadcast raw <text> | broadcast from <Name> <text>",
            ["kick"] = "@Name kick [seconds] [reason] | @Name kick cancel",
            ["psoffer"] = "psoffer",
            ["pstake"] = "pstake <product id>",
            ["psfee"] = "psfee [price]",
            ["storespent"] = "storespent [lumens]",
            ["psreview"] = "psreview <product id> <approve|reject>",
            ["followdigest"] = "followdigest [dry] [hours]",
            ["abilitystats"] = "abilitystats",
            ["whoami"] = "whoami",
            ["landscape"] = "landscape <zone id>",
            ["eventexpire"] = "eventexpire [confirm <zone>]",
            ["landscapes"] = "landscapes",
            ["ensureclaim"] = "ensureclaim",
            ["claimhere"] = "claimhere",
            ["sitename"] = "sitename <name>",
            ["expiresites"] = "expiresites all | expiresites <site id> [site id ...]",
            ["unclaim"] = "unclaim",
            ["matname"] = "matname <n>",
            ["cellcompare"] = "cellcompare [on|off]",
            ["vertexonly"] = "vertexonly [on|off]",
            ["veinsweep"] = "veinsweep",
            ["observed"] = "observed [operation] [size m]",
            ["lodbuild"] = "lodbuild",
            ["tpclaim"] = "tpclaim",
            ["abilities"] = "abilities [item id ...]",
            ["uievent"] = "uievent <id>",
            ["portraitcrc"] = "portraitcrc <crc hex>",
            ["portraittest"] = "portraittest <character guid> <Headshot|Portrait> <image set id>",
            ["vendoropen"] = "vendoropen <vendor guid hex>",
            ["storetoken"] = "storetoken",
            ["luacode"] = "luacode <Lua code>",
            ["luas"] = "luas <Table.Method> [arg ...]",
            ["fte"] = "fte <id> | fte clear",
            ["stream"] = "stream",
            ["pioneer"] = "pioneer",
            ["claims"] = "claims",
            ["craft"] = "craft <recipe> <count>",
            ["composite"] = "composite <effect id>",
            ["setres"] = "setres <resource id> <type> <value>",
            ["claimbasic"] = "claimbasic",
            ["beginclaim"] = "beginclaim",
            ["resupply"] = "resupply",
            ["founderchest"] = "founderchest",
            ["activate"] = "activate <item id>",
            ["use"] = "use <item id>",
            ["cancelbuff"] = "cancelbuff <buff name>",
            ["ratevote"] = "ratevote <claim id>",
            ["sitemusic"] = "sitemusic <ClaimMusic id>",
            ["paletteslot"] = "paletteslot <slot> <type> <value>",
            ["game"] = "game [rules]",
            ["gamejoin"] = "gamejoin",
            ["gameleave"] = "gameleave",
            ["gameedit"] = "gameedit [off]",
            ["gamestart"] = "gamestart",
            ["gametp"] = "gametp [on|off]",
            ["finderwaiting"] = "finderwaiting [table guid hex [off]|off]",
            ["spirefull"] = "spirefull [on|off]",
            ["gamelabels"] = "gamelabels [on|off]",
            ["story"] = "story",
            ["storyfire"] = "storyfire <prop guid hex> <event id>",
            ["proptext"] = "proptext <prop hex> [text]",
            ["propname"] = "propname <prop hex> [name]",
            ["propread"] = "propread <prop hex>",
            ["npctext"] = "npctext <mode> [text]",
            ["propshowtext"] = "propshowtext <prop hex> <mode> [seconds]",
            ["propset"] = "propset <prop hex> <setting key> <value>",
            ["storyfaction"] = "storyfaction <creature guid hex> <0 enemy|1 neutral|2 ally>",
            ["storyweapons"] = "storyweapons <creature guid hex> [1 equipped|0 sheathed]",
            ["creatureweapon"] = "creatureweapon <creature guid hex|near> [Wield_*.adr|off] [tint alias]",
            ["storylink"] = "storylink <source hex> <event> <target hex> <action>",
            ["storyclick"] = "storyclick <guid hex>",
            ["playmode"] = "playmode [on|off]",
            ["gamepoint"] = "gamepoint",
            ["sysmsg"] = "sysmsg id <text id> [name=value ...] | sysmsg key <key or text> | sysmsg chan7 <text>",
            ["contestphase"] = "contestphase <id> <entries|voting|judging>",
            ["contests"] = "contests",
            ["contestenter"] = "contestenter [contest name]",
            ["gamescore"] = "gamescore <points>",
            ["gameend"] = "gameend",
            ["itemstats"] = "itemstats [on|off]",
            ["closecraft"] = "closecraft",
            ["cancelcraft"] = "cancelcraft",
            ["station"] = "station <station id>",
            ["chestopen"] = "chestopen",
            ["chestput"] = "chestput <item> <count>",
            ["chestpickup"] = "chestpickup",
            ["casterinfo"] = "casterinfo <ability>",
            ["clearprops"] = "clearprops",
            ["beginprop"] = "beginprop <prop id> | beginprop #<item id>",
            ["propslide"] = "propslide <guid hex> <dx> <dy> <dz> [seconds]",
            ["placeat"] = "placeat <x> <y> <z> [yaw] | placeat here [yaw]",
            ["anim"] = "anim <AnimationSelection id> [dx dz]",
            ["outpost"] = "outpost <n>",
            ["sight"] = "sight",
            ["pathing"] = "pathing [on|off]",
            ["sitepathing"] = "sitepathing [on|off]",
            ["wall"] = "wall <dx> <dz> <length m> <height m> <x|z> [door m] [material]",
            ["outpostwake"] = "outpostwake <n>",
            ["outpostuse"] = "outpostuse <n> <chest|crystal>",
            ["barrenrecipes"] = "barrenrecipes [drop]",
            ["gc"] = "gc",
            ["harvestfail"] = "harvestfail <reason> [resource]",
            ["lootbag"] = "lootbag <item> [count]",
            ["lootfree"] = "lootfree <item> [count]",
            ["chest"] = "chest [go|open]",
            ["graveyard"] = "graveyard [go] [cavern|surface]",
            ["terrainmem"] = "terrainmem",
            ["terminal"] = "terminal <hub|c<index>|hex node> [open 0|1]",
            ["grantaccess"] = "grantaccess <player> <hex mask>",
            ["ownblock"] = "ownblock [material] [edge voxels]",
            ["cavecheck"] = "cavecheck [zone|all]",
            ["cellstats"] = "cellstats [x y z]",
            ["clearcell"] = "clearcell <hex cell id>",
            ["weapons"] = "weapons",
            ["propremove"] = "propremove <guid>",
            ["weartint"] = "weartint [alias]",
            ["proptint"] = "proptint <guid> [alias]",
            ["propdelete"] = "propdelete <guid>",
            ["propmenu"] = "propmenu [guid hex|near]",
            ["propenable"] = "propenable <guid hex> [0|1]",
            ["durabilityof"] = "durabilityof <item def>",
            ["psinstances"] = "psinstances [clear]",
            ["setwear"] = "setwear <item def> <wear points>",
            ["autoresupply"] = "autoresupply <item def> [on|off]",
            ["durability"] = "durability <item def> <value>",
            ["resettitles"] = "resettitles",
            ["veins"] = "veins <radius>",
            ["calcstats"] = "calcstats",
            ["npcmode"] = "npcmode <n>",
            ["exportdesign"] = "exportdesign <design guid>",
            ["regendelay"] = "regendelay <seconds>",
            ["caveair"] = "caveair <gx> <gy> <gz>",
            ["veintest"] = "veintest",
            ["nearvein"] = "nearvein [go]",
            ["lapsesite"] = "lapsesite",
            ["fxat"] = "fxat <effect> [metres] [ms]",
            ["sortbag"] = "sortbag",
            ["guildof"] = "guildof",
            ["vaultnear"] = "vaultnear",
            ["vault"] = "vault [a]",
            ["revive"] = "revive",
            ["reviveby"] = "reviveby <dead player>",
            ["god"] = "god [on|off]",
            ["maxplayers"] = "maxplayers [n|full|config]",
            ["friendtpcooldown"] = "friendtpcooldown [seconds]",
            ["landscapecap"] = "landscapecap [n|full|config|text <id>]",
            ["grief"] = "grief [level <n>|points <n>|damage <n>|award <damage>|show|reset]",
            ["wield"] = "wield <item>",
            ["weaponattack"] = "weaponattack <basic|charged|power|special> [times]",
            ["status"] = "status <name> [seconds]",
            ["states"] = "states",
            ["combatgear"] = "combatgear",
            ["itemaudit"] = "itemaudit",
            ["placedesign"] = "placedesign <design guid>",
            ["clearunlocks"] = "clearunlocks <kind> [kind ...]",
            ["propcost"] = "propcost <prop id>",
            ["propmove"] = "propmove <guid hex> <dx> <dy> <dz>",
            ["giveprop"] = "giveprop <prop id>",
            ["copyhere"] = "copyhere <voxels>",
            ["pasteat"] = "pasteat <dx> <dy> <dz>",
            ["offgridpaste"] = "offgridpaste [yaw degrees] [design guid]",
            ["pathquery"] = "pathquery <dx> <dy> <dz>",
            ["undo"] = "undo",
            ["redo"] = "redo",
            ["designoffer"] = "designoffer <design guid hex>",
            ["stampdry"] = "stampdry [n]",
            ["isair"] = "isair [n]",
            ["launch"] = "launch <prop hex>",
            ["prop"] = "prop <prop id> [dx dy dz]",
        };

        /// <summary>A command's usage line, or null for a name that is no command.</summary>
        internal static string UsageOf(string command) => command != null && Usages.TryGetValue(command, out string usage) ? usage : null;

        /// <summary>
        /// How many arguments a command needs at least, read from its usage line: per alternative (split at a "|" outside
        /// brackets), every top-level <c>&lt;…&gt;</c> group and bare word after the command name counts, <c>[…]</c> groups
        /// don't; the smallest alternative wins ("shutdown &lt;seconds&gt; [message] | shutdown cancel" needs 1).
        /// </summary>
        internal static int RequiredArguments(string command)
        {
            if (UsageOf(command) is not string usage)
                return 0;
            int fewest = int.MaxValue;
            foreach (string alternative in SplitTopLevel(usage))
            {
                int count = 0;
                foreach (string token in Tokens(alternative))
                    if (!token.StartsWith('[') && !token.StartsWith('@') && token != command)
                        count++;
                fewest = Math.Min(fewest, count);
            }
            return fewest == int.MaxValue ? 0 : fewest;
        }

        /// <summary>Does this command line ("name args") have fewer arguments than <see cref="RequiredArguments"/>?</summary>
        internal static bool MissingArguments(string[] parts)
        {
            int given = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length : 0;
            return given < RequiredArguments(parts[0]);
        }

        /// <summary>The usage line's alternatives: split at " | " outside &lt;…&gt; and […].</summary>
        private static IEnumerable<string> SplitTopLevel(string usage)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < usage.Length; i++)
            {
                char c = usage[i];
                if (c == '<' || c == '[')
                    depth++;
                else if ((c == '>' || c == ']') && depth > 0)
                    depth--;
                else if (c == '|' && depth == 0)
                {
                    yield return usage[start..i].Trim();
                    start = i + 1;
                }
            }
            yield return usage[start..].Trim();
        }

        /// <summary>One alternative's tokens: whitespace-separated, a bracketed group (with its spaces) staying whole.</summary>
        private static IEnumerable<string> Tokens(string alternative)
        {
            int depth = 0, start = -1;
            for (int i = 0; i <= alternative.Length; i++)
            {
                char c = i < alternative.Length ? alternative[i] : ' ';
                if (c == ' ' && depth == 0)
                {
                    if (start >= 0)
                        yield return alternative[start..i];
                    start = -1;
                    continue;
                }
                if (start < 0)
                    start = i;
                if (c == '<' || c == '[')
                    depth++;
                else if ((c == '>' || c == ']') && depth > 0)
                    depth--;
            }
        }

        /// <summary>An exception a wrong argument causes (not a number / hex / id, too few words for a fixed layout).</summary>
        internal static bool IsBadArgument(Exception exception)
            => exception is FormatException or OverflowException or IndexOutOfRangeException or ArgumentOutOfRangeException;
    }
}
