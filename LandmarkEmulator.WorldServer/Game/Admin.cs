using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The admin menu: accounts or characters named in WorldServer.json / .env "Admins" or in the auth DB <c>admin</c>
    /// table (<see cref="IsAdmin"/>) get a /admin chat command that opens an Admin page in the in-game browser (the
    /// client's own OpenMarketplaceTo(url), global.lua; the page is served by <see cref="WebPages"/> at /admin, opened
    /// with the player's Showcase token once and then by an admin cookie, <see cref="AdminSessions"/>). The page's
    /// buttons run <see cref="DevCommands"/> for the admin or any player online; the commands go through the same queue
    /// as dev_commands.txt and run on the next world tick. Only admins' tokens are accepted, and every command run is
    /// logged with who ran it. The DB list is SQL-managed; .env stays an optional bootstrap.
    /// </summary>
    public static class Admin
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Is this player an admin (an entry in WorldServer.json / .env "Admins" / "AdminList", Docker ADMIN_ACCOUNT /
        /// ADMIN_2 / ADMIN_3 / ADMINS, or in the auth DB <c>admin</c> table)? An entry is <c>account:Name</c> (only the
        /// account username is compared), <c>character:Name</c> (only the character name), or a bare name (either, under
        /// the contest rule of <see cref="Contested"/>).
        /// </summary>
        public static bool IsAdmin(WorldSession session)
        {
            if (session == null)
                return false;
            WorldServerConfiguration config = ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            if (config == null)
                return false;
            foreach (string entry in Entries(config))
            {
                (EntryKind kind, string name) = ParseEntry(entry);
                switch (Match(kind, name, session.Account?.Username, session.CharacterName))
                {
                    case EntryMatch.Yes:
                        return true;
                    case EntryMatch.IfUncontested:
                        bool byAccount = string.Equals(name, session.Account?.Username, StringComparison.OrdinalIgnoreCase);
                        bool byCharacter = string.Equals(name, session.CharacterName, StringComparison.OrdinalIgnoreCase);
                        if (!Contested(session, name, byAccount, byCharacter))
                            return true;
                        break;
                }
            }
            return false;
        }

        /// <summary>
        /// Every admin entry: .env / WorldServer.json <see cref="WorldServerConfiguration.Admins"/> and
        /// <see cref="WorldServerConfiguration.AdminList"/> (bootstrap), then the auth DB <c>admin</c> table. Trimmed,
        /// empty ones left out. Prefer <see cref="Entries(WorldServerConfiguration, IEnumerable{string})"/> in tests.
        /// </summary>
        internal static IEnumerable<string> Entries(WorldServerConfiguration config)
            => Entries(config, DatabaseAdminEntries());

        /// <summary>Union of config bootstrap entries and DB rows (tests pass a fixed DB list).</summary>
        internal static IEnumerable<string> Entries(WorldServerConfiguration config, IEnumerable<string> dbEntries)
            => (config.Admins ?? new List<string>())
                .Concat((config.AdminList ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Concat(dbEntries ?? Enumerable.Empty<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim());

        /// <summary>Auth DB admin rows, or none when the database is not initialised (unit tests).</summary>
        private static IEnumerable<string> DatabaseAdminEntries()
        {
            try
            {
                return Shared.Database.DatabaseManager.Instance.AuthDatabase?.GetAdminEntries()
                    ?? Enumerable.Empty<string>();
            }
            catch (Exception e)
            {
                log.Warn($"Admin DB entries unavailable ({e.Message}); using .env only.");
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>Which name an admin entry names (review 2026-10-18b #2).</summary>
        internal enum EntryKind
        {
            /// <summary>A bare name: an account or a character (the old form, kept so existing configs keep working).</summary>
            Either,
            /// <summary><c>account:Name</c>: only the account username is compared.</summary>
            Account,
            /// <summary><c>character:Name</c>: only the character name is compared.</summary>
            Character,
        }

        internal enum EntryMatch
        {
            No,
            /// <summary>A prefixed entry matched its own namespace: nothing else is consulted.</summary>
            Yes,
            /// <summary>A bare entry matched: admin unless the name is contested (<see cref="Contested"/>).</summary>
            IfUncontested,
        }

        internal const string AccountPrefix = "account:";
        internal const string CharacterPrefix = "character:";

        /// <summary>Splits an entry into its kind and name ("account:Robin" → Account, "Robin"; prefixes ignore case).</summary>
        internal static (EntryKind Kind, string Name) ParseEntry(string entry)
        {
            entry = (entry ?? "").Trim();
            if (entry.StartsWith(AccountPrefix, StringComparison.OrdinalIgnoreCase))
                return (EntryKind.Account, entry[AccountPrefix.Length..].Trim());
            if (entry.StartsWith(CharacterPrefix, StringComparison.OrdinalIgnoreCase))
                return (EntryKind.Character, entry[CharacterPrefix.Length..].Trim());
            return (EntryKind.Either, entry);
        }

        /// <summary>
        /// Does an entry name this player? A prefixed entry is decided by its own namespace alone: account usernames are
        /// unique among accounts and character names among characters, so nobody can take an <c>account:</c> or
        /// <c>character:</c> entry from its owner by registering the name in the other namespace (the denial of service
        /// the bare-name contest rule allows: any player can register a free account named like the admin's character,
        /// and the bare entry then makes nobody an admin; review 2026-10-18b #2).
        /// </summary>
        internal static EntryMatch Match(EntryKind kind, string name, string accountName, string characterName)
        {
            if (string.IsNullOrEmpty(name))
                return EntryMatch.No;
            bool byAccount = string.Equals(name, accountName, StringComparison.OrdinalIgnoreCase);
            bool byCharacter = string.Equals(name, characterName, StringComparison.OrdinalIgnoreCase);
            return kind switch
            {
                EntryKind.Account => byAccount ? EntryMatch.Yes : EntryMatch.No,
                EntryKind.Character => byCharacter ? EntryMatch.Yes : EntryMatch.No,
                _ => byAccount || byCharacter ? EntryMatch.IfUncontested : EntryMatch.No,
            };
        }

        /// <summary>
        /// The bare-name rule (entries without <c>account:</c> / <c>character:</c>, kept so existing configs keep working).
        /// A bare entry is a name in either of two namespaces that players fill themselves: account names (anyone may
        /// register a free one, /srp/register) and character names (anyone may create a free one). Matching either let a
        /// player become admin by taking the configured name in the other namespace — registering an account called
        /// like the admin's character ("ADMIN_ACCOUNT=RobinHood" naming the character), or creating a character called
        /// like the admin's account — and with it every dev command for every player (Admin.Run, AdminCommands, the
        /// /admin web menu). A bare entry that names this player's account while a character of another account has that
        /// name, or this player's character while an account of someone else has it, is contested: it makes nobody an
        /// admin until the owner fixes the list (logged once per name, with the prefixed form to use). Neither namespace
        /// can be trusted over the other, as either side may be the impostor; the prefixes let the owner say which one is
        /// meant, and the other namespace is then never consulted. Looked up in the databases at most once a minute per
        /// (entry, account).
        /// </summary>
        private static bool Contested(WorldSession session, string entry, bool byAccount, bool byCharacter)
        {
            // Both: this player's account and character carry the name; names are unique in each namespace, so no one
            // else holds it.
            if (byAccount && byCharacter)
                return false;
            ulong account = session.Account?.Id ?? 0;
            var key = (entry.ToLowerInvariant(), account, byAccount);
            DateTime now = DateTime.UtcNow;
            if (contested.TryGetValue(key, out var cached) && now - cached.At < ContestedRecheck)
                return cached.Contested;
            bool result;
            try
            {
                result = byAccount
                    ? Shared.Database.DatabaseManager.Instance.CharacterDatabase.FindCharacterByName(entry) is { } other
                        && other.AccountId != account
                    : Shared.Database.DatabaseManager.Instance.AuthDatabase.AccountExists(entry);
            }
            catch (Exception e)
            {
                // The database is away: no admin rights on a name that can't be checked (the last answer stays cached).
                log.Warn($"Admin entry '{entry}' could not be checked ({e.Message}); not an admin for now.");
                return cached.At != default ? cached.Contested : true;
            }
            contested[key] = (result, now);
            if (result && (!cached.Contested || cached.At == default))
                log.Error($"Admin entry '{entry}' names {(byAccount ? "account" : "character")} {entry} of {session.CharacterName}, "
                    + $"but {(byAccount ? "a character of another account" : "another account")} has the same name: the entry is "
                    + "ignored (nobody gets admin from it). Write it as "
                    + $"'{(byAccount ? AccountPrefix : CharacterPrefix)}{entry}' so only the {(byAccount ? "account" : "character")} "
                    + "is checked (docs/admin.md), or rename the impostor.");
            else if (!result && cached.At == default)
                log.Info($"Admin entry '{entry}' has no '{AccountPrefix}' / '{CharacterPrefix}' prefix, so another player can take "
                    + "it away by registering the name in the other namespace; write it as "
                    + $"'{(byAccount ? AccountPrefix : CharacterPrefix)}{entry}' (docs/admin.md).");
            return result;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Entry, ulong Account, bool ByAccount),
            (bool Contested, DateTime At)> contested = new();
        private static readonly TimeSpan ContestedRecheck = TimeSpan.FromMinutes(1);

        /// <summary>World entry: admins get the /admin command (bound in their client) and a line saying so.</summary>
        public static void Enter(WorldSession session)
        {
            if (!IsAdmin(session) || WorldChannelServices.Commerce.ShowcaseToken(session) is not string token)
                return;
            string url = $"{WorldChannelServices.Commerce.ShowcaseUrl}/admin?t={token}";
            ClientLua.RunCode(session, $"BindCommandToLua('admin', \"OpenMarketplaceTo('{url}')\")");
            Chat.System(session, "You are an admin: type /admin to open the admin menu.");
            log.Info($"{session.CharacterName} is an admin.");
        }

        /// <summary>Runs a dev command from the admin page for a target ("me", "all" or a character name).</summary>
        public static (bool Ok, string Message) Run(WorldSession admin, string target, string command)
        {
            if (!IsAdmin(admin))
                return (false, "Not an admin.");
            command = (command ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (command.Length == 0)
                return (false, "No command.");
            string who = string.IsNullOrWhiteSpace(target) || target == "me" ? admin.CharacterName : target.Trim();
            string line = who == "all" ? command : $"@{who} {command}";
            DevCommands.Enqueue(line, admin);
            log.Info($"Admin {admin.CharacterName} ran: {line}");
            return (true, $"Ran \"{command}\" for {(who == "all" ? "everyone" : who)}.");
        }

        /// <summary>Who is online, for the page's target list.</summary>
        public static object Players() => PlayerManager.Snapshot()
            .Where(p => !p.Disconnected)
            .Select(p => new { name = p.CharacterName, landscape = p.ZoneId })
            .OrderBy(p => p.name).ToList();

        /// <summary>Named <c>creature</c> kinds for the admin menu's spawn list (<see cref="Creatures.AdminSpawnKinds"/>).</summary>
        public static object CreatureKinds() => Creatures.AdminSpawnKinds();

        /// <summary>Most item rows returned by <see cref="Items"/> (same cap as finditem's log lines).</summary>
        public const int ItemSearchCap = 40;

        /// <summary>
        /// Item categories that have at least one definition, for the admin menu's filter: id, display name (from the
        /// ReferenceData dump when known), and how many items carry that category.
        /// </summary>
        public static object ItemCategories()
        {
            EnsureCategoryNames();
            var counts = new Dictionary<uint, int>();
            foreach (ItemDefinitionManager.ItemDefinition def in ItemDefinitionManager.All)
            {
                foreach (uint category in def.Categories)
                    counts[category] = counts.GetValueOrDefault(category) + 1;
            }
            return counts
                .OrderBy(kv => CategoryLabel(kv.Key), StringComparer.OrdinalIgnoreCase)
                .ThenBy(kv => kv.Key)
                .Select(kv => new { id = kv.Key, name = CategoryLabel(kv.Key), count = kv.Value })
                .ToList();
        }

        /// <summary>One row of <see cref="Items"/> for the admin menu's picker.</summary>
        public sealed record ItemHit(uint Id, string Name, IReadOnlyList<uint> Categories);

        /// <summary>Result of <see cref="Items"/>: up to <see cref="ItemSearchCap"/> hits plus totals.</summary>
        public sealed record ItemSearch(IReadOnlyList<ItemHit> Items, int Matched, int Shown);

        /// <summary>
        /// Item definitions for the admin menu's picker: name fragment or <c>#id</c>, optional category id. At most
        /// <see cref="ItemSearchCap"/> rows; empty needle with no category returns no rows (avoids dumping the whole list).
        /// </summary>
        public static ItemSearch Items(string q, string category)
        {
            q = (q ?? "").Trim();
            uint? cat = uint.TryParse(category, out uint categoryId) ? categoryId : null;
            if (q.Length == 0 && cat == null)
                return new ItemSearch(Array.Empty<ItemHit>(), 0, 0);
            bool byId = q.StartsWith('#');
            string needle = byId ? q[1..] : q;
            int matched = 0, shown = 0;
            var rows = new List<ItemHit>();
            foreach (ItemDefinitionManager.ItemDefinition def in ItemDefinitionManager.All)
            {
                if (cat is uint needCat && !def.Categories.Contains(needCat))
                    continue;
                string name = TextManager.Instance.GetTextForId(def.NameId) ?? "";
                bool hit = q.Length == 0
                    || (byId ? needle == def.Id.ToString() : name.Contains(needle, StringComparison.OrdinalIgnoreCase));
                if (!hit)
                    continue;
                matched++;
                if (shown >= ItemSearchCap)
                    continue;
                shown++;
                rows.Add(new ItemHit(def.Id, name, def.Categories));
            }
            return new ItemSearch(rows, matched, shown);
        }

        private static readonly object categoryNamesGate = new();
        private static Dictionary<uint, string> categoryNames;

        private static string CategoryLabel(uint id)
        {
            EnsureCategoryNames();
            if (categoryNames.TryGetValue(id, out string name) && !string.IsNullOrEmpty(name))
                return name;
            return "Category " + id;
        }

        /// <summary>
        /// Category id → name from <c>39-ReferenceDataItemCategoryDefinitions</c> (25-byte records: id, id, u32, name
        /// text id, u32, u32, u8). Loaded once; missing dump or text leaves "Category N".
        /// </summary>
        private static void EnsureCategoryNames()
        {
            if (categoryNames != null)
                return;
            lock (categoryNamesGate)
            {
                if (categoryNames != null)
                    return;
                var names = new Dictionary<uint, string>();
                try
                {
                    string path = Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps",
                        "39-ReferenceDataItemCategoryDefinitions");
                    if (File.Exists(path))
                    {
                        byte[] data = File.ReadAllBytes(path);
                        // Opcode (4) + count (4), then count × 25-byte records.
                        if (data.Length >= 8)
                        {
                            uint count = BitConverter.ToUInt32(data, 4);
                            for (uint i = 0; i < count; i++)
                            {
                                int off = 8 + (int)i * 25;
                                if (off + 25 > data.Length)
                                    break;
                                uint id = BitConverter.ToUInt32(data, off);
                                uint nameId = BitConverter.ToUInt32(data, off + 12);
                                string text = TextManager.Instance.GetTextForId(nameId);
                                names[id] = string.IsNullOrEmpty(text) ? null : text;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Warn($"Admin item category names unavailable ({e.Message}).");
                }
                categoryNames = names;
            }
        }

        /// <summary>The admin page (ES5 only: the in-game browser is old).</summary>
        public const string Page = """
<!doctype html>
<html><head><meta charset="utf-8"><title>Admin</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;background:#1b1d22;color:#e6e6e6;margin:0;padding:14px;font-size:14px}
h1{font-size:20px;margin:0 0 10px}h2{font-size:15px;margin:16px 0 6px;color:#9fc6ff;border-bottom:1px solid #333;padding-bottom:3px}
button{background:#2d5d9f;color:#fff;border:0;border-radius:4px;padding:6px 10px;margin:2px;cursor:pointer;font-size:13px}
button.off{background:#5a2d2d}button:hover{filter:brightness(1.2)}
button.chip{background:#3a3f4a;font-size:12px;padding:4px 8px}
input,select{background:#2a2d34;color:#eee;border:1px solid #444;border-radius:4px;padding:5px;margin:2px;font-size:13px}
.row{margin:4px 0}#msg{position:sticky;top:0;background:#24324a;padding:6px 10px;border-radius:4px;min-height:18px;margin-bottom:8px;z-index:1}
.note{color:#999;font-size:12px}
#itemhits{max-height:180px;overflow:auto;border:1px solid #333;border-radius:4px;padding:4px;margin:4px 0;background:#22252b}
#itemhits .hit{display:block;width:100%;text-align:left;background:transparent;color:#e6e6e6;border:0;border-bottom:1px solid #333;border-radius:0;padding:6px 4px;margin:0;cursor:pointer;font-size:12px}
#itemhits .hit:hover{background:#2d5d9f}
#cmdhist{margin:4px 0}
</style></head><body>
<h1>Admin</h1>
<div id="msg">Ready.</div>
<div class="row">Target: <select id="target"><option value="me">Me</option><option value="all">Everyone</option></select>
<button onclick="players()">Refresh players</button></div>
<div class="row"><button onclick="location.href='/admin/monitor'">Server monitor</button>
<span class="note">zone stats, update times, services, site games, procedural data, packets (read-only)</span></div>
<div class="row"><button onclick="location.href='/admin/settings'">Server settings</button>
<span class="note">every server setting, explained; change them here instead of in .env, and restart the world</span></div>

<h2>Me / target</h2>
<div class="row"><button onclick="run('god on')">God mode on</button><button class="off" onclick="run('god off')">God mode off</button>
<button onclick="run('revive')">Revive</button><button onclick="ask('coins','Coins to give (e.g. +1000)')">Coins…</button></div>
<div class="row">Give item: <input id="item" size="7" placeholder="item id"> x <input id="count" size="4" value="1">
<button onclick="giveItem()">Give</button></div>
<div class="row">Category: <select id="icat"><option value="">All categories</option></select>
<input id="find" size="18" placeholder="name or #id"><button onclick="searchItems()">Search</button>
<span class="note">Enter submits; click a hit to fill the id</span></div>
<div id="itemhits" class="note">Search or pick a category.</div>

<h2>Travel</h2>
<div class="row"><button onclick="run('spire')">Spire</button><button onclick="run('tpclaim')">My build site</button>
<button onclick="run('cave')">Random cave</button><button onclick="run('landscapes')">Landscapes window</button></div>
<div class="row">To player: <input id="tpto" size="12" placeholder="name"><button onclick="run('tpto '+v('tpto'))">Go</button>
Landscape id: <input id="zone" size="5" placeholder="464"><button onclick="run('landscape '+v('zone'))">Go</button></div>

<h2>Building</h2>
<div class="row"><button onclick="run('claimhere')">Claim a build site here</button>
<button onclick="run('playmode on')">Play mode on</button><button class="off" onclick="run('playmode off')">Play mode off</button>
<button onclick="run('give 105 1')">Build Site Flag</button></div>

<h2>World</h2>
<div class="row">Time of day: <input id="hour" size="4" placeholder="12"><button onclick="run('timeofday '+v('hour'))">Set</button>
<button onclick="run('timeofday 6')">Dawn</button><button onclick="run('timeofday 12')">Noon</button><button onclick="run('timeofday 19')">Dusk</button><button onclick="run('timeofday 0')">Night</button></div>
<div class="row">Creature: <select id="kind"><option value="kobold">Loading…</option></select>
<button onclick="run('creature '+v('kind'))">Spawn near target</button><button class="off" onclick="run('clearcreatures')">Clear creatures</button>
<span class="note">p&lt;prop id&gt; still works in Any command</span></div>
<div class="row">Server shutdown in <input id="secs" size="4" value="60"> s <input id="why" size="24" placeholder="reason (optional)">
<button class="off" onclick="shutdownGo()">Start countdown</button>
<button onclick="run('shutdown cancel')">Cancel</button></div>
<div class="row">Broadcast: <input id="bc" size="40" placeholder="message to everyone online">
<button onclick="broadcastGo()">System Broadcast</button>
<span class="note">"System Broadcast: …" on screen and in chat, to everyone</span></div>

<h2>Moderation (target player)</h2>
<div class="row">Kick in <input id="ksecs" size="4" value="30"> s <input id="kwhy" size="30" placeholder="reason (optional)">
<button class="off" onclick="kick()">Kick target</button><button onclick="if(one())run('kick cancel')">Cancel kick</button>
<span class="note">warnings during the countdown, then a message box and the connection closes; 0 s = now</span></div>

<h2>Player Studio: staff review</h2>
<div class="row"><button onclick="psqueue()">Refresh</button>
<span class="note">designs the peer reviewers rejected; approve lists them, reject turns them down (the creator gets the result mail)</span></div>
<div id="psq" class="note">Not loaded.</div>

<h2>Any command</h2>
<div class="row"><input id="cmd" size="50" placeholder="dev command, e.g. give 231 10"><button onclick="runCmd()">Run</button></div>
<div id="cmdhist" class="note"></div>
<p class="note">Up/Down cycles recent commands (kept in this browser). Commands run on the next server tick; full list in DevCommands.cs.</p>

<script>
var csrf='__CSRF__';
var hist=[], histIdx=-1, histDraft='';
function v(id){return document.getElementById(id).value;}
function say(s){document.getElementById('msg').innerHTML=s;}
function call(path,cb,extra){var x=new XMLHttpRequest();x.open('POST','/api/'+path);x.setRequestHeader('Content-Type','application/x-www-form-urlencoded');
x.onload=function(){try{cb(JSON.parse(x.responseText));}catch(e){say('Bad answer: '+x.status);}};x.onerror=function(){say('Server not reachable.');};
var body='csrf='+encodeURIComponent(csrf);if(extra)body+='&'+extra;x.send(body);}
function runFor(tg,c){call('admin/run?target='+encodeURIComponent(tg)+'&cmd='+encodeURIComponent(c),function(r){say(r.message);});}
function run(c){runFor(v('target'),c);}
function enter(id,fn){var el=document.getElementById(id);if(!el)return;el.onkeydown=function(ev){ev=ev||window.event;var k=ev.keyCode||ev.which;if(k===13){if(ev.preventDefault)ev.preventDefault();fn();return false;}};}
function giveItem(){var id=v('item').replace(/^\s+|\s+$/g,'');if(!id){say('Type or pick an item id first.');return;}run('give '+id+' '+v('count'));}
function broadcastGo(){if(v('bc'))runFor('all','broadcast '+v('bc'));}
function shutdownGo(){if(confirm('Shut the server down?'))run('shutdown '+v('secs')+(v('why')?' '+v('why'):''));}
function one(){var tg=v('target');if(tg=='me'||tg=='all'){say('Pick one player as the target first.');return false;}return true;}
function kick(){if(!one())return;var s=parseInt(v('ksecs'),10);if(isNaN(s)||s<0)s=0;
if(confirm('Kick '+v('target')+(s>0?' in '+s+' s':' now')+'?'))run('kick '+s+(v('kwhy')?' '+v('kwhy'):''));}
function ask(c,q){var a=prompt(q);if(a)run(c+' '+a);}
function players(){call('admin/players',function(r){if(!r.ok){say(r.message);return;}var s=document.getElementById('target'),keep=s.value;
s.innerHTML='<option value="me">Me</option><option value="all">Everyone</option>';
for(var i=0;i<r.players.length;i++){var o=document.createElement('option');o.value=r.players[i].name;o.text=r.players[i].name+' ('+r.players[i].landscape+')';s.appendChild(o);}
s.value=keep;say('Players: '+r.players.length+' online.');});}
function esc(s){return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');}
function decide(id,how){call('admin/run?target=me&cmd='+encodeURIComponent('psreview '+id+' '+how),function(r){say(r.message);setTimeout(psqueue,1500);});}
function psqueue(){call('admin/psqueue',function(r){var d=document.getElementById('psq');if(!r.ok){d.innerHTML=esc(r.message);return;}
if(!r.products.length){d.innerHTML='Nothing waiting for staff.';return;}var h='';
for(var i=0;i<r.products.length;i++){var p=r.products[i];
h+='<div class="row"><b>'+esc(p.name)+'</b> by '+esc(p.creator)+' &middot; '+p.price+' &middot; '+p.approvals+' up / '+p.rejections+' down &middot; '+esc(p.created)
+'<br>'+esc(p.description)+(p.tags?' <i>['+esc(p.tags)+']</i>':'')+(p.reasons?'<br>Rejected for: '+esc(p.reasons):'')
+'<br><button onclick="decide(\''+p.id+'\',\'approve\')">Approve</button><button class="off" onclick="decide(\''+p.id+'\',\'reject\')">Reject</button></div>';}
d.innerHTML=h;});}
function loadCategories(){call('admin/item-categories',function(r){if(!r.ok){say(r.message);return;}var s=document.getElementById('icat');
s.innerHTML='<option value="">All categories</option>';
for(var i=0;i<r.categories.length;i++){var c=r.categories[i],o=document.createElement('option');o.value=String(c.id);o.text=c.name+' ('+c.count+')';s.appendChild(o);}
});}
function loadCreatures(){call('admin/creatures',function(r){if(!r.ok){say(r.message);return;}var s=document.getElementById('kind'),keep=s.value;
s.innerHTML='';
for(var i=0;i<r.creatures.length;i++){var c=r.creatures[i],o=document.createElement('option');o.value=c.id;o.text=c.name+' ('+c.id+')';s.appendChild(o);}
if(keep)s.value=keep;if(!s.value&&s.options.length)s.selectedIndex=0;});}
function searchItems(){var q=v('find').replace(/^\s+|\s+$/g,''),cat=v('icat');
if(!q&&!cat){say('Type a name fragment or #id, or pick a category.');return;}
call('admin/items',function(r){if(!r.ok){say(r.message);return;}var d=document.getElementById('itemhits');
if(!r.items.length){d.innerHTML='No matches.';say('Items: 0 matches.');return;}
var h='';
for(var i=0;i<r.items.length;i++){var it=r.items[i];
h+='<button type="button" class="hit" onclick="pickItem('+it.id+')">#'+it.id+' — '+esc(it.name)+'</button>';}
if(r.matched>r.shown)h+='<div class="note">Showing '+r.shown+' of '+r.matched+'.</div>';
d.innerHTML=h;say('Items: '+r.matched+' match(es).');},'q='+encodeURIComponent(q)+'&category='+encodeURIComponent(cat));}
function pickItem(id){document.getElementById('item').value=String(id);say('Item id '+id+' ready to Give.');}
function histLoad(){try{hist=JSON.parse(localStorage.getItem('lmAdminCmdHistory')||'[]');if(!(hist instanceof Array))hist=[];}catch(e){hist=[];}}
function histSave(){try{localStorage.setItem('lmAdminCmdHistory',JSON.stringify(hist.slice(-50)));}catch(e){}}
function histPush(c){if(hist.length&&hist[hist.length-1]===c){histIdx=-1;histDraft='';histRecent();return;}
hist.push(c);if(hist.length>50)hist=hist.slice(-50);histSave();histIdx=-1;histDraft='';histRecent();}
function histRecent(){var d=document.getElementById('cmdhist');if(!d)return;var h='',start=Math.max(0,hist.length-8);
for(var i=hist.length-1;i>=start;i--)h+='<button type="button" class="chip" onclick="histFill('+i+')">'+esc(hist[i])+'</button> ';
d.innerHTML=h||'<span class="note">No recent commands yet.</span>';}
function histFill(i){document.getElementById('cmd').value=hist[i];}
function runCmd(){var c=v('cmd').replace(/^\s+|\s+$/g,'');if(!c){say('Type a command first.');return;}
call('admin/run?target='+encodeURIComponent(v('target'))+'&cmd='+encodeURIComponent(c),function(r){say(r.message);if(r.ok)histPush(c);});}
function cmdKey(ev){ev=ev||window.event;var k=ev.keyCode||ev.which;
if(k===13){if(ev.preventDefault)ev.preventDefault();runCmd();return false;}
if(k!==38&&k!==40)return;
if(k===38){if(histIdx<0){histDraft=v('cmd');histIdx=hist.length;}if(histIdx>0){histIdx--;document.getElementById('cmd').value=hist[histIdx];}}
else{if(histIdx<0)return;histIdx++;if(histIdx>=hist.length){histIdx=-1;document.getElementById('cmd').value=histDraft;}
else document.getElementById('cmd').value=hist[histIdx];}
if(ev.preventDefault)ev.preventDefault();return false;}
enter('item',giveItem);enter('count',giveItem);enter('find',searchItems);
enter('tpto',function(){run('tpto '+v('tpto'));});enter('zone',function(){run('landscape '+v('zone'));});
enter('hour',function(){run('timeofday '+v('hour'));});enter('bc',broadcastGo);
enter('secs',shutdownGo);enter('why',shutdownGo);enter('ksecs',kick);enter('kwhy',kick);
document.getElementById('cmd').onkeydown=cmdKey;
document.getElementById('icat').onchange=function(){searchItems();};
histLoad();histRecent();
players();
psqueue();
loadCategories();
loadCreatures();
</script></body></html>
""";
    }
}
