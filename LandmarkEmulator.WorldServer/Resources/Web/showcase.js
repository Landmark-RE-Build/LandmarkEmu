// Showcase pages (the client's InGameMarketplaceBrowser window). ES5 only: the client's browser is Chrome 33.
// Routes are the client's own: #!/marketplace, #!/gallery, #!/marketplace/search/...&SearchTags=<tags>.
// The server signs a player in by loading this page with ?t=<token>; the token is kept in sessionStorage (one per
// client process) and sent with every request.
(function () {
  'use strict';

  var token = (function () {
    var m = /[?&]t=([0-9a-f]+)/.exec(location.search);
    try {
      if (m) sessionStorage.setItem('lmemuToken', m[1]);
      return m ? m[1] : (sessionStorage.getItem('lmemuToken') || '');
    } catch (e) {
      return m ? m[1] : '';
    }
  })();

  var catalogue = null, gallery = null;
  var state = { view: 'marketplace', group: null, search: '', shown: 0 };
  var PAGE = 120;

  function $(id) { return document.getElementById(id); }
  function esc(text) {
    return String(text == null ? '' : text).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function request(method, url, data, done) {
    var xhr = new XMLHttpRequest();
    var form = [];
    data = data || {};
    data.t = token;
    for (var k in data) if (data.hasOwnProperty(k)) form.push(encodeURIComponent(k) + '=' + encodeURIComponent(data[k]));
    var body = form.join('&');
    xhr.open(method, method === 'GET' ? url + '?' + body : url, true);
    if (method === 'POST') xhr.setRequestHeader('Content-Type', 'application/x-www-form-urlencoded');
    xhr.onreadystatechange = function () {
      if (xhr.readyState !== 4) return;
      var result = null;
      try { result = JSON.parse(xhr.responseText); } catch (e) { result = null; }
      done(result, xhr.status);
    };
    xhr.send(method === 'POST' ? body : null);
  }

  var toastTimer = null;
  function toast(text, bad) {
    var t = $('toast');
    t.textContent = text;
    t.className = bad ? 'bad' : '';
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.className = 'hidden'; }, 3500);
  }

  function icon(id, cls) {
    return id ? '<img src="/icons/' + id + '.png" onerror="this.className=\'noicon\';this.removeAttribute(\'src\')">'
              : '<div class="noicon"></div>';
  }

  // ---- routing ----
  function route() {
    var hash = location.hash || '#!/marketplace';
    var tags = /SearchTags=([^&]*)/.exec(hash);
    if (hash.indexOf('#!/gallery') === 0) state.view = 'gallery';
    else if (hash.indexOf('#!/myvendors') === 0) state.view = 'myvendors';
    else if (hash.indexOf('#!/help') === 0) state.view = 'help';
    else state.view = 'marketplace';
    if (tags) {
      state.search = decodeURIComponent(tags[1]).replace(/[,;]/g, ' ');
      $('search').value = state.search;
      state.group = null;
    }
    $('tab-marketplace').className = state.view === 'marketplace' ? 'on' : '';
    $('tab-gallery').className = state.view === 'gallery' ? 'on' : '';
    $('tab-myvendors').className = state.view === 'myvendors' ? 'on' : '';
    $('tab-help').className = state.view === 'help' ? 'on' : '';
    closeDetail();
    render();
  }

  function render() {
    if (state.view === 'gallery') renderGallery();
    else if (state.view === 'myvendors') renderVendors();
    else if (state.view === 'help') renderHelp();
    else renderStore();
  }

  // ---- store ----
  function loadCatalogue() {
    request('GET', '/api/catalogue', null, function (items) {
      catalogue = items || [];
      render();
    });
  }

  function matches(item) {
    if (state.group && item.section + '/' + item.group !== state.group) return false;
    if (!state.search) return true;
    var words = state.search.toLowerCase().split(/\s+/);
    var text = (item.name + ' ' + item.group + ' ' + item.section + ' ' + item.description).toLowerCase();
    for (var i = 0; i < words.length; i++) if (words[i] && text.indexOf(words[i]) < 0) return false;
    return true;
  }

  function renderStore() {
    if (!catalogue) { $('content').innerHTML = '<p class="note">Loading the store...</p>'; $('side').innerHTML = ''; return; }
    // Side list: sections and their groups with counts.
    var groups = {}, order = [];
    for (var i = 0; i < catalogue.length; i++) {
      var it = catalogue[i], key = it.section + '/' + it.group;
      if (!groups[key]) { groups[key] = { section: it.section, group: it.group, count: 0 }; order.push(key); }
      groups[key].count++;
    }
    var side = '<a href="javascript:void 0" data-group="" class="' + (state.group ? '' : 'on') + '">Everything<span>' + catalogue.length + '</span></a>';
    var section = null;
    for (var j = 0; j < order.length; j++) {
      var g = groups[order[j]];
      if (g.section !== section) { section = g.section; side += '<div class="section">' + esc(section) + '</div>'; }
      side += '<a href="javascript:void 0" data-group="' + esc(order[j]) + '" class="' + (state.group === order[j] ? 'on' : '') + '">'
        + esc(g.group) + '<span>' + g.count + '</span></a>';
    }
    $('side').innerHTML = side;

    var list = [];
    for (var k = 0; k < catalogue.length; k++) if (matches(catalogue[k])) list.push(catalogue[k]);
    state.list = list;
    state.shown = Math.min(PAGE, list.length);
    var title = state.group ? state.group.split('/')[1] : (state.search ? 'Search: ' + state.search : 'Everything');
    $('content').innerHTML = '<h1 class="heading">' + esc(title) + '</h1>'
      + '<p class="note">Everything in the store is free on this server. ' + list.length + ' item' + (list.length === 1 ? '' : 's') + '.</p>'
      + '<div class="cards" id="cards"></div><div id="morebox"></div>';
    appendCards(0, state.shown);
  }

  function appendCards(from, to) {
    var html = '';
    for (var i = from; i < to; i++) {
      var it = state.list[i];
      html += '<div class="card" data-item="' + it.id + '">' + icon(it.icon) + '<div class="name">' + esc(it.name) + '</div>'
        + '<div class="price">Free</div><button data-take="' + it.id + '">Get</button></div>';
    }
    $('cards').insertAdjacentHTML('beforeend', html);
    $('morebox').innerHTML = to < state.list.length ? '<button class="alt more" id="more">Show more (' + (state.list.length - to) + ' left)</button>' : '';
  }

  function find(id) {
    for (var i = 0; i < catalogue.length; i++) if (catalogue[i].id === id) return catalogue[i];
    return null;
  }

  function showDetail(id) {
    var it = find(id);
    if (!it) return;
    var counts = it.section === 'Decorations' ? '<select id="count"><option>1</option><option>5</option><option>10</option><option>25</option></select>' : '';
    var d = $('detail');
    d.innerHTML = icon(it.icon) + '<h2>' + esc(it.name) + '</h2><div class="group">' + esc(it.section + ' · ' + it.group) + '</div>'
      + '<div class="desc">' + esc(it.description || '') + '</div>'
      + '<div class="buttons">' + counts + '<button data-take="' + it.id + '">Get (free)</button> <button class="alt" id="close">Close</button></div>';
    d.className = '';
  }

  function closeDetail() { $('detail').className = 'hidden'; }

  function take(id) {
    if (!token) { toast('Open the Showcase from the game to sign in.', true); return; }
    var countBox = $('count');
    var count = countBox && $('detail').className !== 'hidden' ? countBox.value : 1;
    request('POST', '/api/take', { item: id, count: count }, function (r) {
      toast(r ? r.message : 'The server did not answer.', !(r && r.ok));
    });
  }

  // ---- gallery ----
  function loadGallery() {
    request('GET', '/api/gallery', null, function (sites) {
      gallery = sites || [];
      if (state.view === 'gallery') render();
    });
  }

  function renderGallery() {
    $('side').innerHTML = '<div class="section">Gallery</div><a href="javascript:void 0" class="on">Build sites<span>'
      + (gallery ? gallery.length : '') + '</span></a>';
    if (!gallery) { $('content').innerHTML = '<p class="note">Loading the gallery...</p>'; loadGallery(); return; }
    var html = '<h1 class="heading">Build sites</h1><p class="note">Every build site on this server, best rated first. Visit takes you there.</p>';
    var shown = 0;
    for (var i = 0; i < gallery.length; i++) {
      var s = gallery[i];
      var text = (s.name + ' ' + s.owner + ' ' + s.landscape + ' ' + s.description).toLowerCase();
      if (state.search && text.indexOf(state.search.toLowerCase()) < 0) continue;
      shown++;
      html += '<div class="site"><div class="info"><div class="title">' + esc(s.name || 'Unnamed build site') + '</div>'
        + '<div class="meta"><span class="dot' + (s.online ? ' on' : '') + '"></span>' + esc(s.owner) + ' · ' + esc(s.landscape) + '</div>'
        + (s.description ? '<div class="desc">' + esc(s.description) + '</div>' : '') + '</div>'
        + '<div class="rating">' + s.rating + ' vote' + (s.rating === 1 ? '' : 's') + '</div>'
        + '<button data-visit="' + s.id + '">Visit</button></div>';
    }
    if (!shown) html += '<p class="note">No build sites match.</p>';
    $('content').innerHTML = html;
  }

  function visit(id) {
    if (!token) { toast('Open the Showcase from the game to sign in.', true); return; }
    request('POST', '/api/visit', { site: id }, function (r) {
      toast(r ? r.message : 'The server did not answer.', !(r && r.ok));
    });
  }

  // ---- my vendors (build site vendors: the owner's side, which the client has no screen for) ----
  var mine = null;
  function loadVendors() {
    request('GET', '/api/myvendors', null, function (r) {
      mine = r || { vendors: [], bag: [], coins: 0, fee: 0.1 };
      if (state.view === 'myvendors') renderVendors();
    });
  }
  function renderVendors() {
    $('side').innerHTML = '<div class="section">My vendors</div><a href="javascript:void 0" class="on">Vendors<span>'
      + (mine ? mine.vendors.length : '') + '</span></a>';
    if (!token) { $('content').innerHTML = '<p class="note">Open the Showcase from the game to sign in.</p>'; return; }
    if (!mine) { $('content').innerHTML = '<p class="note">Loading your vendors...</p>'; loadVendors(); return; }
    var html = '<h1 class="heading">My vendors</h1><p class="note">Put items from your bag up for sale on a Build Site Vendor. '
      + 'Visitors buy them with coins; what they pay reaches you by mail. Listing costs ' + Math.round(mine.fee * 100)
      + '% of the price. You have ' + mine.coins + ' coins.</p>';
    if (!mine.vendors.length) {
      html += '<p class="note">You have no vendor placed. Get a Build Site Vendor from the Store (Decorations, Build Site Vendor) '
        + 'and place it on your build site.</p>';
      $('content').innerHTML = html;
      return;
    }
    for (var i = 0; i < mine.vendors.length; i++) {
      var v = mine.vendors[i];
      html += '<div class="vendor"><h2>Vendor ' + (i + 1) + (v.landscape ? ' &middot; ' + esc(v.landscape) : '') + '</h2>';
      if (!v.stock.length) html += '<p class="note">Nothing for sale yet.</p>';
      for (var j = 0; j < v.stock.length; j++) {
        var l = v.stock[j];
        html += '<div class="row">' + icon(l.info.icon) + '<div class="nm">' + esc(l.info.name) + '</div><div class="ct">&times; ' + l.count + '</div>'
          + '<input id="p' + l.id + '" value="' + l.price + '"> coins <button class="alt" data-price="' + l.id + '">Set price</button>'
          + '<button class="alt" data-unlist="' + l.id + '">Take back</button></div>';
      }
      html += '</div>';
    }
    var options = '';
    for (var k = 0; k < mine.vendors.length; k++) options += '<option value="' + mine.vendors[k].id + '">Vendor ' + (k + 1) + '</option>';
    html += '<div class="vendor"><h2>Your bag</h2>'
      + (mine.vendors.length > 1 ? '<p class="note">List on <select class="vsel" id="vsel">' + options + '</select></p>' : '');
    if (!mine.bag.length) html += '<p class="note">Your bag is empty.</p>';
    for (var b = 0; b < mine.bag.length; b++) {
      var it = mine.bag[b];
      html += '<div class="row">' + icon(it.info.icon) + '<div class="nm">' + esc(it.info.name) + '</div><div class="ct">&times; ' + it.count + '</div>'
        + '<input id="c' + it.guid + '" value="' + it.count + '" title="how many"> &times; <input id="q' + it.guid + '" value="10" title="coins each"> coins '
        + '<button data-list="' + it.guid + '">List</button></div>';
    }
    $('content').innerHTML = html + '</div>';
  }
  function vendorChoice() { var s = $('vsel'); return s ? s.value : mine.vendors[0].id; }
  function afterVendor(r) { toast(r ? r.message : 'The server did not answer.', !(r && r.ok)); loadVendors(); }

  // ---- help (the game's Help / Support link opens this page: ClientSettings help URL) ----
  function renderHelp() {
    $('side').innerHTML = '<div class="section">Help</div><a href="javascript:void 0" class="on">This server</a>';
    $('content').innerHTML = '<div class="help"><h1 class="heading">Help</h1>'
      + '<p>This is a fan-run Landmark server. Most of the game works: build sites, building, crafting, gathering, combat, '
      + 'site games, story tools, friends, groups, guilds, mail and trade.</p>'
      + '<h2>Showcase</h2><ul><li><b>Store</b>: outfits, decorations and designs, all free. Get puts the item in your bag.</li>'
      + '<li><b>Gallery</b>: every build site on the server. Visit takes you there.</li>'
      + '<li><b>My vendors</b>: put items from your bag up for sale on a Build Site Vendor (from the Store, Decorations). '
      + 'Visitors buy with coins and the coins reach you by mail.</li></ul>'
      + '<h2>Chat commands</h2><ul>'
      + '<li><code>/invite Name</code>, <code>/party leave</code>, <code>/party kick Name</code>: groups.</li>'
      + '<li><code>/friend add Name</code>: friends list (window: K).</li>'
      + '<li><code>/guild_debug create Name</code>, <code>/guildinvite Name</code>, <code>/guildquit</code>, '
      + '<code>/guildmotd text</code>: guilds (window: N).</li>'
      + '<li><code>/emotelist</code> and <code>/wave</code>, <code>/dance</code> ...: emotes.</li></ul>'
      + '<h2>Build sites</h2><ul><li>Place a Build Site Flag to claim land; open its management window to rename it, set '
      + 'permissions and music.</li><li>F5 switches your site between Edit and Play Mode: Play Mode runs the story tools '
      + '(Links) and site games.</li></ul>'
      + '<h2>Something wrong?</h2><p>Use the in-game bug report (Escape menu). Reports are saved on the server for its '
      + 'admins to read.</p></div>';
  }

  // ---- events ----
  document.addEventListener('click', function (e) {
    var el = e.target;
    while (el && el !== document && !(el.getAttribute && (el.getAttribute('data-take') || el.getAttribute('data-visit')
      || el.getAttribute('data-list') || el.getAttribute('data-price') || el.getAttribute('data-unlist')
      || el.getAttribute('data-item') || el.getAttribute('data-group') !== null || el.id === 'more' || el.id === 'close'))) {
      el = el.parentNode;
    }
    if (!el || el === document) return;
    if (el.getAttribute('data-take')) { take(+el.getAttribute('data-take')); return; }
    if (el.getAttribute('data-visit')) { visit(el.getAttribute('data-visit')); return; }
    if (el.getAttribute('data-list')) {
      var g = el.getAttribute('data-list');
      request('POST', '/api/vendor/list', { vendor: vendorChoice(), item: g, count: $('c' + g).value, price: $('q' + g).value }, afterVendor);
      return;
    }
    if (el.getAttribute('data-price')) {
      var p = el.getAttribute('data-price');
      request('POST', '/api/vendor/price', { listing: p, price: $('p' + p).value }, afterVendor);
      return;
    }
    if (el.getAttribute('data-unlist')) { request('POST', '/api/vendor/unlist', { listing: el.getAttribute('data-unlist') }, afterVendor); return; }
    if (el.id === 'more') { var from = state.shown; state.shown = Math.min(from + PAGE, state.list.length); appendCards(from, state.shown); return; }
    if (el.id === 'close') { closeDetail(); return; }
    if (el.getAttribute('data-item')) { showDetail(+el.getAttribute('data-item')); return; }
    if (el.getAttribute('data-group') !== null) { state.group = el.getAttribute('data-group') || null; render(); }
  });

  var searchTimer = null;
  $('search').addEventListener('input', function () {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(function () { state.search = $('search').value.trim(); render(); }, 200);
  });
  document.addEventListener('keydown', function (e) { if (e.keyCode === 27) closeDetail(); });
  window.addEventListener('hashchange', route);

  request('GET', '/api/me', null, function (me) {
    $('who').innerHTML = me && me.signedIn ? 'Signed in as <b>' + esc(me.name) + '</b>' : 'Not signed in';
  });
  loadCatalogue();
  route();
})();
