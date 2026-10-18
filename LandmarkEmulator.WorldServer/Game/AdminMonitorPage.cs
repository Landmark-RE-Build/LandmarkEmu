namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The web page /admin/monitor (admins only, by the admin cookie like /admin, <see cref="AdminSessions"/>): the server-side view of the
    /// client's admin monitor pages (trace/architecture/admin-pages.md §6), read-only. It polls /api/admin/monitor
    /// (<see cref="AdminMonitor.Snapshot"/>) every few seconds while open and loads nothing from outside the world's
    /// web server. ES5 only: the in-game browser is old.
    /// </summary>
    public static class AdminMonitorPage
    {
        public const string Html = """
<!doctype html>
<html><head><meta charset="utf-8"><title>Server monitor</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;background:#1b1d22;color:#e6e6e6;margin:0;padding:14px;font-size:13px}
h1{font-size:20px;margin:0 0 8px}h2{font-size:15px;margin:18px 0 6px;color:#9fc6ff;border-bottom:1px solid #333;padding-bottom:3px}
table{border-collapse:collapse;margin:4px 0}td,th{padding:2px 8px;text-align:left;border-bottom:1px solid #2c2f36;white-space:nowrap}
th{color:#aaa;font-weight:normal}td.n,th.n{text-align:right}
#bar{position:sticky;top:0;background:#24324a;padding:6px 10px;border-radius:4px;margin-bottom:8px}
.note{color:#999;font-size:12px}.bad{color:#ff6b6b}.ok{color:#7ee787}
pre{margin:2px 0;font-family:Consolas,monospace;font-size:12px}
a{color:#9fc6ff}button{background:#2d5d9f;color:#fff;border:0;border-radius:4px;padding:4px 9px;margin-left:6px;cursor:pointer}
</style></head><body>
<h1>Server monitor</h1>
<div id="bar">Loading… <button onclick="paused=!paused;this.innerHTML=paused?'Resume':'Pause'">Pause</button></div>
<div id="out"></div>
<p class="note">Read-only. The live client had these as admin monitor pages (Alt+F: zone stats, zone profiler, gateway,
procedural data, instanced games, pathing); the retail client can't open them, so they are shown here. Refreshes every 3 s while
this page is open. <a id="back" href="#">Back to the admin menu</a></p>
<script>
document.getElementById('back').href='/admin';
var paused=false,prev=null,prevAt=0;
var colours=['#ff7777','#ffff00','#00ff00','#00ffff','#7777ff','#ff77ff'];
function e(s){return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');}
function table(head,rows){var h='<table><tr>';for(var i=0;i<head.length;i++){var n=head[i].charAt(0)=='#';h+='<th'+(n?' class="n"':'')+'>'+e(n?head[i].substring(1):head[i])+'</th>';}
h+='</tr>';for(var r=0;r<rows.length;r++){h+='<tr>';for(var c=0;c<rows[r].length;c++){var v=rows[r][c];var num=typeof v=='number';h+='<td'+(num?' class="n"':'')+'>'+(v&&v.html?v.html:e(v))+'</td>';}h+='</tr>';}
return h+'</table>';}
function dur(s){var h=Math.floor(s/3600),m=Math.floor(s%3600/60);return h+'h '+m+'m';}
function render(d){
var z=d.zone,h='',i;
var fps=z.tickAvgMs>0?1000/z.tickAvgMs:0;
document.getElementById('bar').innerHTML='Up '+dur(d.uptimeSeconds)+' · '+z.players+' players · tick avg '+z.tickAvgMs+' ms, max '+z.tickMaxMs+' ms, '+z.ticksPerSecond+' ticks/s'
+(d.world.shutdownPending?' · <span class="bad">shutdown pending</span>':'')+' <button onclick="paused=!paused;this.innerHTML=paused?\'Resume\':\'Pause\'">'+(paused?'Resume':'Pause')+'</button>';
h+='<h2>ZONE STATS</h2>'+table(['Landscape','Name','#Players','#Creatures','#Props'],z.landscapes.map(function(l){return [l.id,l.name,l.players,l.creatures,l.props];}));
h+='<p class="note">Window '+z.windowSeconds+' s, '+z.ticks+' ticks (each tick sleeps 1 ms after its work). Zone server id '+d.world.zoneServerId+'.</p>';
h+='<h2>ZONE PROFILER (update systems, last window)</h2>'+table(['Name','#Count','#AvgUs','#MinUs','#MaxUs','#Total ms'],d.systems.map(function(s){return [s.name,s.count,s.avgUs,s.minUs,s.maxUs,s.totalMs];}));
var L=d.links;
h+='<h2>Services</h2>'+table(['Service','State'],[['Gateway',L.gateway],['Social',L.social],['World channel',L.worldChannel],['Commerce',L.commerce]]);
(d.services||[]).forEach(function(s){h+='<h3>'+e(s.service)+(s.stale?' (stale: '+s.ageSeconds+' s old)':'')+'</h3>'+table(['What','Value'],s.rows.map(function(r){return [r.key,r.value];}));});
h+='<h2>Terrain (Environment Server)</h2>'+table(['What','Value'],[['Store',d.terrain.store],['Cells healed since start',d.terrain.healedCells],['Last heal',d.terrain.lastHeal||'-']]);
h+='<h2>Instanced Game Information - Server</h2>';
if(!d.games.length)h+='<p class="note">No site games.</p>';
for(i=0;i<d.games.length;i++){var g=d.games[i];for(var j=0;j<g.length;j++){var l=g[j],pad='';for(var k=1;k<l.indent*2;k++)pad+=' ';
h+='<pre style="color:'+colours[l.indent%6]+'">'+e((l.indent>0?pad+'-':'')+l.key+' '+l.value)+'</pre>';}}
h+='<p class="note">Admins standing on a site game also get this as the client\'s page 29 when the server setting AdminMonitorPush is on ('+(d.world.pushPage29?'on':'off')+').</p>';
var P=d.procedural;
h+='<h2>Procedural data</h2><p class="note">Placement sets: '+(P.enabled?'on':'off')+', '+P.sets+' loaded. Respawn: '+e(P.respawn||'')+'. Per landscape with players: nodes standing of the candidate spots (min / max: the whole landscape once its census is in; in place: the regions generated so far), minimums and respawns queued.</p>';
for(i=0;i<P.landscapes.length;i++){var pl=P.landscapes[i];h+='<p>'+e(pl.name)+' ('+pl.landscape+'): Zone Spawn Count '+pl.standing+' of '+pl.nodes+' nodes, minimum '+pl.min+', '+pl.belowMin+' type(s) below it, '+pl.queued+' queued</p>'
+table(['#Set','#Group','#Biome','Mask','#Standing','#Min','#Nodes','#Queued'],pl.sets.map(function(s){return [s.set<0?'tree list':s.set,s.group,s.biome,s.mask,s.standing,s.min,s.nodes,s.queued];}));}
var Q=d.pathing;
h+='<h2>Pathing Information - Zone</h2>';
if(!Q.hasData)h+='<p class="note">Nothing to display at this time. (Creature pathing '+(Q.enabled?'on':'off')+'.)</p>';
else{h+='<p class="note">Creature navigation (pathing '+(Q.enabled?'on':'off')+'): walkable column tiles of 19.2 m built on first need, paths searched on a background thread. The names are the client\'s page 32; what each counts here is ours.</p>'
+table(['Pathing Summary','#Value'],[['NavMeshCells (tiles cached, of '+Q.maxTiles+')',Q.navMeshCells],['PendingStitchSections (tiles waiting)',Q.pendingStitchSections],['Characters (creatures with a path)',Q.characters],
['DynamicSilhouettes',Q.dynamicSilhouettes],['AvoidanceObstacles',Q.avoidanceObstacles],['NavMeshJobsRemaining',Q.navMeshJobsRemaining],['NavMeshJobsCompletedThisFrame (per second)',Q.navMeshJobsCompletedThisFrame],
['NavMeshJobsCompletedLifetime',Q.navMeshJobsCompletedLifetime],['Tile memory (KB)',Q.tileKb],['Paths: queries / reached / partial / none / refused',Q.paths.queries+' / '+Q.paths.reached+' / '+Q.paths.partial+' / '+Q.paths.none+' / '+Q.paths.refused],['Average nodes per path',Q.paths.avgNodes],['Build-site grids (site pathing '+(Q.sites.enabled?'on':'off')+'): held / ready / builds / failures / avg ms',Q.sites.held+' / '+Q.sites.ready+' / '+Q.sites.builds+' / '+Q.sites.failures+' / '+Q.sites.avgMs]])
+table(['Name','#Count','#AvgUs','#MinUs','#MaxUs'],Q.timings.map(function(r){return [r.name,r.count,r.avgUs,r.minUs,r.maxUs];}));}
var now=new Date().getTime(),secs=prev?(now-prevAt)/1000:0,old={};
if(prev)for(i=0;i<prev.length;i++)old[prev[i].dir+prev[i].bytes]=prev[i].totalBytes;
h+='<h2>PACKET MONITOR (zone packets by first bytes, since '+e(d.packets.since)+')</h2>'+table(['Dir','Bytes','Name','#Count','#Total(b)','#Avg(b)','#Worst(b)','#Rate(b/s)'],
d.packets.rows.map(function(r){var o=old[r.dir+r.bytes];return [r.dir,r.bytes,r.name,r.count,r.totalBytes,r.avgBytes,r.worstBytes,secs>0&&o!=null?Math.round((r.totalBytes-o)/secs):'-'];}));
prev=d.packets.rows;prevAt=now;
h+='<h2>Players</h2>'+table(['Name','#Landscape','#X','#Y','#Z','Admin'],d.players.map(function(p){return [p.name,p.landscape,p.x,p.y,p.z,p.admin?'yes':''];}));
document.getElementById('out').innerHTML=h;}
function load(){if(paused)return;var x=new XMLHttpRequest();x.open('GET','/api/admin/monitor');
x.onload=function(){try{var d=JSON.parse(x.responseText);if(x.status!=200){document.getElementById('bar').innerHTML=e(d.message||('Error '+x.status));return;}render(d);}catch(err){document.getElementById('bar').innerHTML='Bad answer: '+x.status;}};
x.onerror=function(){document.getElementById('bar').innerHTML='Server not reachable.';};x.send();}
load();setInterval(load,3000);
</script></body></html>
""";
    }
}
