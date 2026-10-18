using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The admin Settings page (/admin/settings, opened from the /admin menu's "Server settings" button; admins only, by
    /// the admin cookie like every admin page, <see cref="AdminSessions"/>): every world setting grouped as in .env.example, with its .env and
    /// WorldServer.json names, the value in effect, where it comes from (default / WorldServer.json / .env / page), its
    /// default, live's value, the help text and when a change counts; edited with toggles, number boxes and drop-downs
    /// (<see cref="ServerSettings"/>). Served by <see cref="WebPages"/>:
    /// <list type="bullet">
    /// <item>GET /admin/settings: the page (its anti-forgery token written into it);</item>
    /// <item>GET /api/admin/settings: the settings as JSON (<see cref="View"/>; secrets only as "set" / "not set");</item>
    /// <item>POST /api/admin/settings/save: set.&lt;key&gt;=value and reset.&lt;key&gt;=1 fields, with csrf;</item>
    /// <item>POST /api/admin/settings/restart: seconds, message, with csrf: the graceful shutdown countdown (Docker's restart
    /// policy starts the world again).</item>
    /// </list>
    /// Every POST needs the admin cookie, the page's anti-forgery token (<see cref="CsrfToken"/>, an HMAC of the cookie's
    /// session under a key made at start-up: another site can't read it, and the cookie is SameSite=Strict too) and, when
    /// the browser sends an Origin header, this server's own origin. ES5 only: the in-game browser is old.
    /// </summary>
    public static class ServerSettingsPage
    {
        public const string PagePath = "/admin/settings";
        public const string ApiPath = "/api/admin/settings";
        public const string SavePath = "/api/admin/settings/save";
        public const string RestartPath = "/api/admin/settings/restart";

        /// <summary>The largest save request read (the rest of the web pages take 4 KB).</summary>
        public const int MaxSaveBody = 64 * 1024;

        private static readonly byte[] csrfKey = RandomNumberGenerator.GetBytes(32);

        /// <summary>The admin pages' anti-forgery token for an admin web session (<see cref="AdminSessions"/>; empty for none).</summary>
        public static string CsrfToken(string adminSession)
        {
            if (string.IsNullOrEmpty(adminSession))
                return "";
            using var hmac = new HMACSHA256(csrfKey);
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("settings:" + adminSession))).ToLowerInvariant();
        }

        /// <summary>Does a request's anti-forgery token belong to this admin web session? (constant-time compare)</summary>
        public static bool CsrfValid(string adminSession, string csrf)
        {
            if (string.IsNullOrEmpty(adminSession) || string.IsNullOrEmpty(csrf))
                return false;
            byte[] expected = Encoding.ASCII.GetBytes(CsrfToken(adminSession));
            byte[] given = Encoding.ASCII.GetBytes(csrf.Trim().ToLowerInvariant());
            return expected.Length == given.Length && CryptographicOperations.FixedTimeEquals(expected, given);
        }

        /// <summary>
        /// A browser's Origin header must name this server (http://host[:port] as the Host header says); none is
        /// accepted (the in-game browser and older browsers send none on same-origin requests).
        /// </summary>
        public static bool OriginAllowed(string origin, string host)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return true;
            if (string.IsNullOrWhiteSpace(host) || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out Uri o))
                return false;
            return string.Equals(o.Authority, host.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The page with its anti-forgery token.</summary>
        public static string Page(string adminSession) => Html.Replace("__CSRF__", CsrfToken(adminSession));

        /// <summary>Is the world running in a container (then Docker's restart policy starts it again after a stop)?</summary>
        public static bool InDocker => File.Exists("/.dockerenv");

        private static string ApplyName(SettingApply apply) => apply switch
        {
            SettingApply.Now => "now",
            SettingApply.NextLogin => "login",
            _ => "restart"
        };

        private static string TypeName(SettingDefinition def)
            => def.Rule.Choices != null ? "choice" : ServerSettings.IsBool(def.Type) ? "bool"
                : ServerSettings.IsInteger(def.Type) ? "int" : ServerSettings.IsReal(def.Type) ? "number" : "text";

        /// <summary>
        /// The page's data. A secret is never in it: only whether it is set. Groups follow .env.example's sections, then the
        /// settings that are only in WorldServer.json; .env variables the world doesn't read (other containers) are listed
        /// by name and help only.
        /// </summary>
        public static object View(SettingsStore store)
        {
            IReadOnlyDictionary<string, SettingOverride> saved = store.Saved;
            var sectionOrder = ServerSettingsDocs.Env.Values.Select(v => v.Section).Distinct().ToList();
            var rows = ServerSettings.Definitions.Select(def =>
            {
                bool secret = def.Access == SettingAccess.Secret;
                saved.TryGetValue(def.Key, out SettingOverride entry);
                (string below, string source) = store.Below(def);
                string running = store.Running(def);
                return new
                {
                    def.Group, def.Order,
                    Row = (object)new
                    {
                        key = def.Key,
                        env = def.EnvNames,
                        type = TypeName(def),
                        access = def.Access.ToString().ToLowerInvariant(),
                        apply = ApplyName(def.Rule.Apply),
                        value = secret ? null : running,
                        isSet = secret ? !string.IsNullOrEmpty(running) : (bool?)null,
                        configured = secret ? null : store.AfterRestart(def),
                        below = secret ? null : below,
                        source = entry != null ? "page" : source,
                        saved = entry?.Value,
                        by = entry?.By,
                        at = entry == null ? null : entry.At.ToString("yyyy-MM-dd HH:mm") + " UTC",
                        @default = secret ? null : def.Default,
                        live = def.Rule.Live,
                        choices = def.Rule.Choices,
                        min = def.Rule.Min,
                        max = def.Rule.Max,
                        format = def.Rule.FormatHint,
                        maxLength = def.Rule.MaxLength,
                        help = def.Help,
                        note = def.Rule.Note,
                        pending = store.RestartPending(def),
                    }
                };
            }).ToList();
            var groups = rows.GroupBy(r => r.Group)
                .OrderBy(g => g.Key == ServerSettings.NoEnvGroup ? int.MaxValue : sectionOrder.IndexOf(g.Key) is int i and >= 0 ? i : int.MaxValue - 1)
                .Select(g => new { name = g.Key, settings = g.OrderBy(r => r.Order).Select(r => r.Row).ToList() })
                .ToList();
            var used = new HashSet<string>(ServerSettings.Definitions.SelectMany(d => d.EnvNames), StringComparer.Ordinal);
            var other = ServerSettingsDocs.Env.Values.Where(v => !used.Contains(v.Name))
                .Select(v => new { env = v.Name, section = v.Section, help = v.Help }).ToList();
            return new
            {
                ok = true,
                file = store.FilePath,
                docker = InDocker,
                groups,
                other,
                restartPending = ServerSettings.Definitions.Where(store.RestartPending).Select(d => d.Key).ToList(),
            };
        }

        /// <summary>An answer: a status with a JSON body or the page's HTML, and work for the world thread (null = none).</summary>
        public sealed record Result(int Status, object Body, string Html = null, Action WorldWork = null);

        private static Result Fail(int status, string message) => new(status, new { ok = false, message });

        /// <summary>
        /// One request to the page or its API. <paramref name="admin"/> is the admin's character name (null = the token is
        /// not an online admin's: refused). GETs need only that; a POST also needs the page's anti-forgery token, an Origin
        /// of this server (or none) and its whole body. A save is checked and written here (all or nothing); the changes
        /// that count now and the restart countdown come back as <see cref="Result.WorldWork"/>.
        /// </summary>
        public static Result Handle(string method, string path, IReadOnlyDictionary<string, string> form, string origin, string host,
            bool bodyComplete, string adminSession, string admin, SettingsStore store)
        {
            if (admin == null)
                return path == PagePath ? new Result(403, null, "Admins only.")
                    : Fail(403, "Admins only (open it from the admin menu while you are in the game).");
            if (store == null)
                return Fail(503, "The settings store is not running.");
            if (method == "GET" && path == PagePath)
                return new Result(200, null, Page(adminSession));
            if (method == "GET" && path == ApiPath)
                return new Result(200, View(store));
            if (method != "POST" || path is not (SavePath or RestartPath))
                return Fail(404, "Not found.");
            if (!OriginAllowed(origin, host))
            {
                log.Warn($"Settings page: {admin}'s {path} refused: Origin {origin} is not this server ({host}).");
                return Fail(403, "Refused: the request did not come from this server's page.");
            }
            if (!CsrfValid(adminSession, form.GetValueOrDefault("csrf")))
            {
                log.Warn($"Settings page: {admin}'s {path} refused: missing or wrong anti-forgery token.");
                return Fail(403, "Refused: the page's security token is missing or old. Reload the page.");
            }
            if (!bodyComplete)
                return Fail(400, "The request did not arrive whole; nothing was saved.");
            if (path == RestartPath)
            {
                if (!int.TryParse(form.GetValueOrDefault("seconds"), out int seconds) || seconds < 0 || seconds > 3600)
                    return Fail(400, "Seconds: 0 to 3600.");
                string text = new string((form.GetValueOrDefault("message") ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
                if (text.Length > 200)
                    text = text[..200];
                if (text.Length == 0)
                    text = "The server is restarting to apply new settings.";
                if (Shutdown.InProgress)
                    return Fail(409, "A shutdown is already counting down (cancel it from the admin menu first).");
                log.Warn($"Admin {admin} restarted the world from the settings page: shutdown in {seconds} s ({text}).");
                return new Result(200, new
                {
                    ok = true,
                    message = $"Shutdown in {seconds} s. " + (InDocker
                        ? "Docker's restart policy starts the world again; reopen /admin once you are back in."
                        : "This world is not in a container: start it again yourself.")
                }, WorldWork: () => Shutdown.Schedule(seconds, text));
            }
            (Dictionary<string, string> set, List<string> reset, List<string> unknown) = ParseSave(form);
            if (unknown.Count > 0)
                return Fail(400, $"Nothing was saved: unknown field(s) {string.Join(", ", unknown.Take(5))}.");
            SettingsStore.SaveResult result = store.Save(admin, set, reset);
            return new Result(result.Ok ? 200 : 400, new
            {
                ok = result.Ok, message = result.Message, errors = result.Errors,
                changes = result.Changes.Select(c => new { key = c.Key, from = c.From, to = c.To, reset = c.Reset, apply = c.Apply.ToString() }),
            }, WorldWork: result.ApplyNow);
        }

        private static readonly NLog.ILogger log = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>Splits a save request's fields: set.&lt;key&gt; → value, reset.&lt;key&gt; → reset.</summary>
        public static (Dictionary<string, string> Set, List<string> Reset, List<string> Unknown) ParseSave(IReadOnlyDictionary<string, string> form)
        {
            var set = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var reset = new List<string>();
            var unknown = new List<string>();
            foreach (var (name, value) in form)
            {
                if (name.StartsWith("set.", StringComparison.Ordinal))
                    set[name[4..]] = value;
                else if (name.StartsWith("reset.", StringComparison.Ordinal))
                    reset.Add(name[6..]);
                else if (name is not ("t" or "csrf"))
                    unknown.Add(name);
            }
            return (set, reset, unknown);
        }

        public const string Html = """
<!doctype html>
<html><head><meta charset="utf-8"><title>Server settings</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;background:#1b1d22;color:#e6e6e6;margin:0;padding:14px;font-size:13px}
h1{font-size:20px;margin:0 0 8px}
h2{font-size:15px;margin:16px 0 6px;color:#9fc6ff;border-bottom:1px solid #333;padding-bottom:3px;cursor:pointer}
h2 .n{color:#888;font-weight:normal;font-size:12px}
button{background:#2d5d9f;color:#fff;border:0;border-radius:4px;padding:5px 10px;margin:2px;cursor:pointer;font-size:13px}
button.off{background:#5a2d2d}button.minor{background:#3a3d45;padding:2px 7px;font-size:12px}button:hover{filter:brightness(1.2)}
button[disabled]{opacity:.45;cursor:default;filter:none}
input,select{background:#2a2d34;color:#eee;border:1px solid #444;border-radius:4px;padding:4px;margin:2px;font-size:13px}
input[type=checkbox]{width:16px;height:16px;vertical-align:middle}
#bar{position:sticky;top:0;z-index:2;background:#24324a;padding:6px 10px;border-radius:4px;margin-bottom:8px}
#msg{min-height:18px}
.set{border-left:3px solid transparent;padding:5px 8px;margin:2px 0;background:#202329;border-radius:3px}
.set.dirty{border-left-color:#e3b341;background:#2a2820}.set.page{border-left-color:#3f7fd1}.set.dirty.page{border-left-color:#e3b341}
.key{font-weight:bold}.env{font-family:Consolas,monospace;color:#9fc6ff;font-size:12px;margin-left:6px}
.chip{display:inline-block;font-size:11px;padding:0 6px;border-radius:8px;margin-left:5px;background:#3a3d45;color:#ccc}
.chip.now{background:#244a2c;color:#9fe0a8}.chip.login{background:#2c3f4a;color:#9fd0e0}.chip.restart{background:#4a3a24;color:#e8c58a}
.chip.ro{background:#3a3a3a;color:#aaa}.chip.secret{background:#4a2430;color:#e8a0b0}.chip.pend{background:#6a4a10;color:#ffd27a}
.chip.live{background:#243a4a;color:#a8d4ff}.chip.notlive{background:#4a2a4a;color:#f0b0f0}
.help{color:#bbb;margin:3px 0 1px}.note{color:#999;font-size:12px}.meta{color:#888;font-size:12px;margin-top:2px}
.val{font-family:Consolas,monospace}
.warn{background:#4a3a1a;border-radius:4px;padding:6px 10px;margin:6px 0}
.err{color:#ff8b8b}
</style></head><body>
<h1>Server settings</h1>
<div id="bar"><div id="msg">Loading…</div>
<span id="count">No unsaved changes.</span>
<button id="save" onclick="save()" disabled>Save changes</button><button id="discard" class="minor" onclick="discard()" disabled>Discard</button>
&nbsp; Search: <input id="q" size="22" placeholder="name, .env name or text" oninput="filter()" onkeyup="filter()">
<label class="note"><input type="checkbox" id="mine" onclick="filter()"> only set on this page</label>
<button class="minor" onclick="fold(false)">Open all</button><button class="minor" onclick="fold(true)">Close all</button>
<button class="minor" onclick="location.href='/admin'">Back to the admin menu</button></div>
<div id="restart"></div>
<div id="out"></div>
<h2 onclick="tog('other')">Other containers <span class="n">(.env lines the world doesn't read: edit .env)</span></h2><div id="g_other" style="display:none"></div>
<p class="note">How it works: a value saved here is kept in the world's data folder (<span id="file" class="val"></span>) and wins over .env and
WorldServer.json (order: code default, WorldServer.json, .env, command line, this page). "Reset to .env" removes the page's value so .env counts
again. <span class="chip now">now</span> = in effect at once; <span class="chip login">next login</span> = in effect at once, players see it when
they next enter the world; <span class="chip restart">restart</span> = after the world restarts. <span class="chip live">live</span> = the value
that was live Landmark's behaviour. Network, ports, addresses, ids, folders, passwords and the admin list are changed in .env only.
Every change is written to the world's log with your name.</p>
<script>
var csrf='__CSRF__';
var data=null,pend={},rows={},closed={};
try{closed=JSON.parse(localStorage.getItem('lmSettingsClosed')||'{}')||{};}catch(e){closed={};}
function e(s){return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');}
function $(id){return document.getElementById(id);}
function say(s,bad){$('msg').innerHTML=bad?'<span class="err">'+s+'</span>':s;}
function id(k){return k.replace(/[^A-Za-z0-9_]/g,'_');}
function shown(v){return v==null||v===''?'(empty)':v;}
function req(method,path,body,cb){var x=new XMLHttpRequest();x.open(method,path);
if(body!=null)x.setRequestHeader('Content-Type','application/x-www-form-urlencoded');
x.onload=function(){var r=null;try{r=JSON.parse(x.responseText);}catch(err){}cb(r||{ok:false,message:'Bad answer: '+x.status},x.status);};
x.onerror=function(){cb({ok:false,message:'Server not reachable.'},0);};x.send(body);}
function load(keepMsg){req('GET','/api/admin/settings',null,function(r){if(!r.ok){say(e(r.message||'Error'),true);return;}data=r;pend={};render();
if(!keepMsg)say('Ready. '+count()+' settings.');});}
function count(){var n=0;for(var i=0;i<data.groups.length;i++)n+=data.groups[i].settings.length;return n;}
function chips(s){var h='';
if(s.access=='secret')h+='<span class="chip secret">secret</span>';else if(s.access=='readonly')h+='<span class="chip ro">edit .env</span>';
else h+='<span class="chip '+s.apply+'">'+(s.apply=='login'?'next login':s.apply)+'</span>';
if(s.live!=null){var same=s.access!='secret'&&String(s.configured)==String(s.live);h+='<span class="chip '+(same?'live':'notlive')+'">live: '+e(shown(s.live))+(same?'':' (differs)')+'</span>';}
if(s.pending)h+='<span class="chip pend">restart needed (running: '+e(shown(s.value))+')</span>';
return h;}
function control(s){var k=id(s.key),v=s.configured==null?'':s.configured;
if(s.access=='secret')return '<span class="val">'+(s.isSet?'set':'not set')+'</span>';
if(s.access!='editable')return '<span class="val">'+e(shown(s.value))+'</span>';
if(s.type=='bool')return '<label><input type="checkbox" id="c_'+k+'"'+(v=='true'?' checked':'')+' onclick="changed(\''+e(s.key)+'\')"> '+(v=='true'?'on':'off')+'</label>';
if(s.type=='choice'){var h='<select id="c_'+k+'" onchange="changed(\''+e(s.key)+'\')">';for(var i=0;i<s.choices.length;i++)
h+='<option'+(s.choices[i].toLowerCase()==String(v).toLowerCase()?' selected':'')+'>'+e(s.choices[i])+'</option>';return h+'</select>';}
if(s.type=='int'||s.type=='number')return '<input type="number" id="c_'+k+'" value="'+e(v)+'" step="'+(s.type=='int'?'1':'any')+'"'
+(s.min!=null?' min="'+s.min+'"':'')+(s.max!=null?' max="'+s.max+'"':'')+' oninput="changed(\''+e(s.key)+'\')" onchange="changed(\''+e(s.key)+'\')" style="width:110px">';
return '<input type="text" id="c_'+k+'" value="'+e(v)+'" size="'+Math.min(70,Math.max(20,String(v).length+4))+'" maxlength="'+s.maxLength+'"'
+' oninput="changed(\''+e(s.key)+'\')" onchange="changed(\''+e(s.key)+'\')"'+(s.format?' placeholder="'+e(s.format)+'"':'')+'>';}
function row(s){rows[s.key]=s;var k=id(s.key);
var h='<div class="set'+(s.saved!=null?' page':'')+'" id="r_'+k+'"><div><span class="key">'+e(s.key)+'</span>';
for(var i=0;i<s.env.length;i++)h+='<span class="env">'+e(s.env[i])+'</span>';
h+=chips(s)+'</div><div>'+control(s)+(s.saved!=null?' <button class="minor" onclick="resetTo(\''+e(s.key)+'\')">Reset to .env</button>':'')+'</div>';
if(s.help)h+='<div class="help">'+e(s.help)+'</div>';if(s.note)h+='<div class="note">'+e(s.note)+'</div>';
var m=[];if(s.access!='secret'){m.push('Default: <span class="val">'+e(shown(s['default']))+'</span>');
m.push('From: '+e(s.source)+(s.saved!=null?' (by '+e(s.by||'?')+', '+e(s.at)+'; .env / json: <span class="val">'+e(shown(s.below))+'</span>)':''));
if(s.access=='editable'&&s.value!=s.configured)m.push('In effect now: <span class="val">'+e(shown(s.value))+'</span>');}
if(s.format)m.push('Form: '+e(s.format));if(s.min!=null||s.max!=null)m.push('Range: '+(s.min!=null?s.min:'')+' to '+(s.max!=null?s.max:''));
h+='<div class="meta">'+m.join(' &middot; ')+'</div></div>';return h;}
function render(){rows={};var h='';for(var i=0;i<data.groups.length;i++){var g=data.groups[i],gid='g'+i;
h+='<h2 onclick="tog(\''+gid+'\')">'+e(g.name)+' <span class="n">('+g.settings.length+')</span></h2><div id="g_'+gid+'" data-name="'+e(g.name)+'"'+(closed[g.name]?' style="display:none"':'')+'>';
for(var j=0;j<g.settings.length;j++)h+=row(g.settings[j]);h+='</div>';}
$('out').innerHTML=h;var o='';for(var i2=0;i2<data.other.length;i2++){var v=data.other[i2];
o+='<div class="set"><span class="env">'+e(v.env)+'</span><span class="chip ro">'+e(v.section)+'</span><div class="help">'+e(v.help)+'</div></div>';}
$('g_other').innerHTML=o;$('file').innerHTML=e(data.file);restartBox();counter();filter();}
function restartBox(){var p=data.restartPending,h='';
h+='<div class="warn">'+(p.length?'<b>Restart needed</b> for '+p.length+' saved setting(s): '+e(p.join(', '))+'.<br>':'')
+'Restart the world: players get the shutdown countdown, are sent to character select and saved, then the world stops; '
+(data.docker?'Docker\'s restart policy starts it again (a minute or two, longer on a first visit to a landscape).':'<b>this world is not in a container: something outside it must start it again.</b>')
+'<br>Countdown <input id="rsecs" type="number" value="60" min="0" max="3600" style="width:70px"> s, message <input id="rmsg" size="36" value="The server is restarting to apply new settings.">'
+'<button class="off" onclick="restart()">Restart world</button></div>';$('restart').innerHTML=h;}
function current(s){var c=$('c_'+id(s.key));if(!c)return null;return s.type=='bool'?(c.checked?'true':'false'):c.value;}
function changed(k){var s=rows[k];if(!s)return;var v=current(s),r=$('r_'+id(k));
if(pend[k]&&pend[k].reset)delete pend[k];
if(String(v)===String(s.configured==null?'':s.configured))delete pend[k];else pend[k]={value:v};
if(s.type=='bool'){var c=$('c_'+id(k));c.parentNode.lastChild.nodeValue=' '+(c.checked?'on':'off');}
r.className=r.className.replace(' dirty','')+(pend[k]?' dirty':'');counter();}
function resetTo(k){var s=rows[k],r=$('r_'+id(k));pend[k]={reset:true};r.className=r.className.replace(' dirty','')+' dirty';
say('"'+e(k)+'" goes back to its .env value ('+e(shown(s.below))+') when you save.');counter();}
function keys(){var a=[];for(var k in pend)if(pend.hasOwnProperty(k))a.push(k);return a;}
function counter(){var n=keys().length;$('count').innerHTML=n?'<b>'+n+' unsaved change(s)</b>':'No unsaved changes.';$('save').disabled=!n;$('discard').disabled=!n;}
function discard(){if(confirm('Discard '+keys().length+' unsaved change(s)?'))load();}
function save(){var a=keys();if(!a.length)return;var body='csrf='+encodeURIComponent(csrf);
for(var i=0;i<a.length;i++){var p=pend[a[i]];body+=p.reset?'&reset.'+encodeURIComponent(a[i])+'=1':'&set.'+encodeURIComponent(a[i])+'='+encodeURIComponent(p.value);}
$('save').disabled=true;say('Saving…');
req('POST','/api/admin/settings/save',body,function(r){if(!r.ok){say(e(r.message),true);$('save').disabled=false;return;}say(e(r.message));load(true);});}
function restart(){var s=parseInt($('rsecs').value,10);if(isNaN(s)||s<0)s=60;var n=keys().length;
if(!confirm((n?'You have '+n+' unsaved change(s); they are lost. ':'')+'Restart the world in '+s+' s? Everyone online gets the countdown and is sent to character select.'))return;
req('POST','/api/admin/settings/restart','csrf='+encodeURIComponent(csrf)+'&seconds='+s+'&message='+encodeURIComponent($('rmsg').value),function(r){say(e(r.message),!r.ok);});}
function tog(gid){var d=$('g_'+gid);if(!d)return;var hide=d.style.display!='none';d.style.display=hide?'none':'';
var n=d.getAttribute('data-name');if(n){closed[n]=hide;try{localStorage.setItem('lmSettingsClosed',JSON.stringify(closed));}catch(err){}}}
function fold(hide){if(!data)return;for(var i=0;i<data.groups.length;i++){var d=$('g_g'+i);d.style.display=hide?'none':'';closed[data.groups[i].name]=hide;}
try{localStorage.setItem('lmSettingsClosed',JSON.stringify(closed));}catch(err){}}
function filter(){if(!data)return;var q=$('q').value.toLowerCase(),mine=$('mine').checked;
for(var i=0;i<data.groups.length;i++){var g=data.groups[i],any=false;
for(var j=0;j<g.settings.length;j++){var s=g.settings[j],txt=(s.key+' '+s.env.join(' ')+' '+(s.help||'')+' '+(s.note||'')).toLowerCase();
var ok=(!q||txt.indexOf(q)>=0)&&(!mine||s.saved!=null||pend[s.key]);$('r_'+id(s.key)).style.display=ok?'':'none';if(ok)any=true;}
var d=$('g_g'+i);d.previousSibling.style.display=any?'':'none';if(q||mine)d.style.display=any?'':'none';else d.style.display=closed[g.name]?'none':'';}}
window.onbeforeunload=function(){if(keys().length)return 'You have unsaved changes.';};
load();
</script></body></html>
""";
    }
}
