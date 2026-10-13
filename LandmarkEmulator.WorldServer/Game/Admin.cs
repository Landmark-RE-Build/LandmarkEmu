using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The admin menu: accounts (or characters) named in WorldServer.json "Admins" get a /admin chat command that opens
    /// an Admin page in the in-game browser (the client's own OpenMarketplaceTo(url), global.lua; the page is served by
    /// <see cref="WebPages"/> at /admin with the player's Showcase token). The page's buttons run <see cref="DevCommands"/>
    /// for the admin or any player online; the commands go through the same queue as dev_commands.txt and run on the
    /// next world tick. Only admins' tokens are accepted, and every command run is logged with who ran it.
    /// </summary>
    public static class Admin
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Is this player an admin (account name or character name in WorldServer.json "Admins")?</summary>
        public static bool IsAdmin(WorldSession session)
        {
            if (session == null)
                return false;
            List<string> admins = ConfigurationManager<WorldServerConfiguration>.Instance.Config?.Admins;
            if (admins == null || admins.Count == 0)
                return false;
            return admins.Any(a => string.Equals(a, session.CharacterName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, session.Account?.Username, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>World entry: admins get the /admin command (bound in their client) and a line saying so.</summary>
        public static void Enter(WorldSession session)
        {
            if (!IsAdmin(session) || Store.TokenOf(session) is not string token)
                return;
            string url = $"{Store.BaseUrl}/admin?t={token}";
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
            DevCommands.Enqueue(line);
            log.Info($"Admin {admin.CharacterName} ran: {line}");
            return (true, $"Ran \"{command}\" for {(who == "all" ? "everyone" : who)}.");
        }

        /// <summary>Who is online, for the page's target list.</summary>
        public static object Players() => PlayerManager.Snapshot()
            .Where(p => !p.Disconnected)
            .Select(p => new { name = p.CharacterName, landscape = p.ZoneId })
            .OrderBy(p => p.name).ToList();

        /// <summary>The admin page (ES5 only: the in-game browser is old).</summary>
        public const string Page = """
<!doctype html>
<html><head><meta charset="utf-8"><title>Admin</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;background:#1b1d22;color:#e6e6e6;margin:0;padding:14px;font-size:14px}
h1{font-size:20px;margin:0 0 10px}h2{font-size:15px;margin:16px 0 6px;color:#9fc6ff;border-bottom:1px solid #333;padding-bottom:3px}
button{background:#2d5d9f;color:#fff;border:0;border-radius:4px;padding:6px 10px;margin:2px;cursor:pointer;font-size:13px}
button.off{background:#5a2d2d}button:hover{filter:brightness(1.2)}
input,select{background:#2a2d34;color:#eee;border:1px solid #444;border-radius:4px;padding:5px;margin:2px;font-size:13px}
.row{margin:4px 0}#msg{position:sticky;top:0;background:#24324a;padding:6px 10px;border-radius:4px;min-height:18px;margin-bottom:8px}
.note{color:#999;font-size:12px}
</style></head><body>
<h1>Admin</h1>
<div id="msg">Ready.</div>
<div class="row">Target: <select id="target"><option value="me">Me</option><option value="all">Everyone</option></select>
<button onclick="players()">Refresh players</button></div>

<h2>Me / target</h2>
<div class="row"><button onclick="run('god on')">God mode on</button><button class="off" onclick="run('god off')">God mode off</button>
<button onclick="run('revive')">Revive</button><button onclick="ask('coins','Coins to give (e.g. +1000)')">Coins…</button></div>
<div class="row">Give item: <input id="item" size="7" placeholder="item id"> x <input id="count" size="4" value="1">
<button onclick="run('give '+v('item')+' '+v('count'))">Give</button>
<input id="find" size="14" placeholder="find item by name"><button onclick="run('finditem '+v('find'))">Find (server log)</button></div>

<h2>Travel</h2>
<div class="row"><button onclick="run('spire')">Spire</button><button onclick="run('tpclaim')">My build site</button>
<button onclick="run('cave')">Random cave</button><button onclick="run('landscapes')">Landscapes window</button></div>
<div class="row">To player: <input id="tpto" size="12" placeholder="name"><button onclick="run('tpto '+v('tpto'))">Go</button>
Landscape id: <input id="zone" size="5" placeholder="417"><button onclick="run('landscape '+v('zone'))">Go</button></div>

<h2>Building</h2>
<div class="row"><button onclick="run('claimhere')">Claim a build site here</button>
<button onclick="run('playmode on')">Play mode on</button><button class="off" onclick="run('playmode off')">Play mode off</button>
<button onclick="run('give 105 1')">Build Site Flag</button></div>

<h2>World</h2>
<div class="row">Time of day: <input id="hour" size="4" placeholder="12"><button onclick="run('timeofday '+v('hour'))">Set</button>
<button onclick="run('timeofday 6')">Dawn</button><button onclick="run('timeofday 12')">Noon</button><button onclick="run('timeofday 19')">Dusk</button><button onclick="run('timeofday 0')">Night</button></div>
<div class="row">Creature: <select id="kind"><option>kobold</option><option>runt</option><option>shaman</option><option>skeleton</option><option>slaug</option><option>ogre</option></select>
<button onclick="run('creature '+v('kind'))">Spawn near target</button><button class="off" onclick="run('clearcreatures')">Clear creatures</button></div>
<div class="row">Server shutdown in <input id="secs" size="4" value="60"> s <button class="off" onclick="if(confirm('Shut the server down?'))run('shutdown '+v('secs'))">Start countdown</button>
<button onclick="run('shutdown cancel')">Cancel</button></div>

<h2>Any command</h2>
<div class="row"><input id="cmd" size="50" placeholder="dev command, e.g. give 231 10"><button onclick="run(v('cmd'))">Run</button></div>
<p class="note">Commands run on the next server tick. The full list is in DevCommands.cs; results show in the server window.</p>

<script>
var t=(location.search.match(/[?&]t=([0-9a-f]+)/)||[])[1]||'';
function v(id){return document.getElementById(id).value;}
function say(s){document.getElementById('msg').innerHTML=s;}
function call(path,cb){var x=new XMLHttpRequest();x.open('POST','/api/'+path+(path.indexOf('?')<0?'?':'&')+'t='+t);
x.onload=function(){try{cb(JSON.parse(x.responseText));}catch(e){say('Bad answer: '+x.status);}};x.onerror=function(){say('Server not reachable.');};x.send();}
function run(c){var tg=v('target');call('admin/run?target='+encodeURIComponent(tg)+'&cmd='+encodeURIComponent(c),function(r){say(r.message);});}
function ask(c,q){var a=prompt(q);if(a)run(c+' '+a);}
function players(){call('admin/players',function(r){if(!r.ok){say(r.message);return;}var s=document.getElementById('target'),keep=s.value;
s.innerHTML='<option value="me">Me</option><option value="all">Everyone</option>';
for(var i=0;i<r.players.length;i++){var o=document.createElement('option');o.value=r.players[i].name;o.text=r.players[i].name+' ('+r.players[i].landscape+')';s.appendChild(o);}
s.value=keep;say('Players: '+r.players.length+' online.');});}
players();
</script></body></html>
""";
    }
}
