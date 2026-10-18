// Showcase pages (the client's InGameMarketplaceBrowser window). ES5 only: the client's browser is Chrome 33.
// Routes are the client's own: #!/marketplace, #!/gallery, #!/marketplace/search/...&SearchTags=<tags>; ours: #!/studio,
// #!/following, #!/myvendors, #!/help.
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

  var catalogue = null, gallery = null, studio = null;
  // The signed-in player (/api/me): Lumens, and whether this server's store is free (WorldServer.json FreeStore).
  var me = { signedIn: false, name: '', lumens: 0, freeStore: false, memberDiscount: 0 };
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

  function num(n) { return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ','); }
  // Members pay less (memberDiscount %, rounded up to 5 Lumens as the server does); the membership row itself doesn't.
  function memberPrice(it) {
    if (!it.price || !me.memberDiscount || it.id === 900600) return it.price;
    return Math.max(5, Math.ceil(it.price * (100 - me.memberDiscount) / 100 / 5) * 5);
  }
  function priceText(it) {
    if (!it.price) return 'Free';
    var p = memberPrice(it);
    return p !== it.price ? '<s>' + num(it.price) + '</s> ' + num(p) + ' Lumens (member)' : num(it.price) + ' Lumens';
  }
  function showWho() {
    $('who').innerHTML = me.signedIn ? 'Signed in as <b>' + esc(me.name) + '</b> · ' + num(me.lumens) + ' Lumens' : 'Not signed in';
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
    else if (hash.indexOf('#!/studio') === 0) state.view = 'studio';
    else if (hash.indexOf('#!/following') === 0) state.view = 'following';
    else if (hash.indexOf('#!/help') === 0) state.view = 'help';
    else state.view = 'marketplace';
    if (tags) {
      // The client joins the words with '+' (Browser.OpenGalleryWithTags FUN_1435e0db0 replaces ' ' by '+'), e.g. the
      // Crimson Parlor's Unlock: SearchTags=Fantasy+Parlor+Pack; decodeURIComponent keeps '+', so it is a space here.
      state.search = decodeURIComponent(tags[1].replace(/\+/g, ' ')).replace(/[,;]/g, ' ');
      $('search').value = state.search;
      state.group = null;
      if (state.view === 'gallery' && galleryTags) {
        // Tag words the client passed (Browser.OpenGalleryWithTags) pick the drop-downs; the rest stays a name search.
        var words = state.search.split(/\s+/), rest = [];
        gstate.tag = ['', '', ''];
        for (var w = 0; w < words.length; w++) {
          var lists = [galleryTags.genre, galleryTags.built, galleryTags.activity], hit = false;
          for (var t = 0; t < 3 && !hit; t++) for (var i = 0; i < lists[t].length; i++) {
            if (lists[t][i].toLowerCase() === words[w].toLowerCase()) { gstate.tag[t] = lists[t][i]; hit = true; break; }
          }
          if (!hit && words[w]) rest.push(words[w]);
        }
        state.search = rest.join(' ');
        $('search').value = state.search;
      }
    }
    $('tab-marketplace').className = state.view === 'marketplace' ? 'on' : '';
    $('tab-gallery').className = state.view === 'gallery' ? 'on' : '';
    $('tab-studio').className = state.view === 'studio' ? 'on' : '';
    $('tab-following').className = state.view === 'following' ? 'on' : '';
    $('tab-myvendors').className = state.view === 'myvendors' ? 'on' : '';
    $('tab-help').className = state.view === 'help' ? 'on' : '';
    closeDetail();
    render();
  }

  function render() {
    if (state.view === 'gallery') renderGallery();
    else if (state.view === 'myvendors') renderVendors();
    else if (state.view === 'studio') renderStudio();
    else if (state.view === 'following') renderFollowing();
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
    var count = list.length + ' item' + (list.length === 1 ? '' : 's') + '.';
    var note = me.freeStore ? 'Everything in the store is free on this server. ' + count
      : 'Prices are in Lumens, as on the live Marketplace (no real money here). ' + count
        + ' Outfits, props and recipes come from the Lumen Station (click your Lumen count) or from exploring.';
    // The inventory's "Get more Lumens" button searches the store for "Lumen" (InventoryDocumentClass.as AddLumensButton).
    if (!me.freeStore && /lumen/i.test(state.search)) {
      note += '</p><p class="note">Lumens are not for sale on this server: earn them by gathering, defeating monsters, '
        + 'opening chests, Lost Crystals and outposts, and completing goals and daily goals.';
    }
    $('content').innerHTML = '<h1 class="heading">' + esc(title) + '</h1>'
      + '<p class="note">' + note + '</p>'
      + '<div class="cards" id="cards"></div><div id="morebox"></div>';
    appendCards(0, state.shown);
  }

  function appendCards(from, to) {
    var html = '';
    for (var i = from; i < to; i++) {
      var it = state.list[i];
      html += '<div class="card" data-item="' + it.id + '">' + icon(it.icon) + '<div class="name">' + esc(it.name) + '</div>'
        + '<div class="price">' + priceText(it) + '</div><button data-take="' + it.id + '">' + (it.price ? 'Buy' : 'Get') + '</button></div>';
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
      + '<div class="group">' + priceText(it) + (it.price ? ' each' : '') + (it.priceNote ? ' (' + esc(it.priceNote) + ')' : '') + '</div>'
      + '<div class="buttons">' + counts + '<button data-take="' + it.id + '">' + (it.price ? 'Buy' : 'Get (free)') + '</button> <button class="alt" id="close">Close</button></div>';
    d.className = '';
  }

  function closeDetail() { $('detail').className = 'hidden'; }

  function take(id) {
    if (!token) { toast('Open the Showcase from the game to sign in.', true); return; }
    var countBox = $('count');
    var count = countBox && $('detail').className !== 'hidden' ? countBox.value : 1;
    request('POST', '/api/take', { item: id, count: count }, function (r) {
      toast(r ? r.message : 'The server did not answer.', !(r && r.ok));
      if (r && typeof r.lumens === 'number') { me.lumens = r.lumens; showWho(); }
    });
  }

  // ---- gallery ----
  // Live's gallery at 0.695 (patch notes 13 Apr - 25 May 2016, trace/0695/gallery-695.md): only sites published from
  // Build Site Management and tagged with all three drop-downs are listed; search by the three tag drop-downs and key
  // words in the site name (the owner's name counts too: "search the gallery for Taelaa"); a filter by Play Mode
  // status; leaderboard (most liked) and rising stars (most recently listed) at the sides; comments on each entry.
  // The server sends the published list once (/api/gallery); everything else is done here.
  var galleryTags = null;
  var gstate = { tag: ['', '', ''], mode: 'any', open: null, comments: {}, loadingComments: {} };
  var LIST = 8;

  function loadGallery() {
    request('GET', '/api/gallery', null, function (sites) {
      gallery = sites || [];
      if (state.view === 'gallery') render();
    });
    if (!galleryTags) request('GET', '/api/gallery/tags', null, function (t) {
      galleryTags = t || { genre: [], built: [], activity: [] };
      if (state.view === 'gallery') render();
    });
  }

  function reloadGallery() { gallery = null; loadGallery(); }

  function siteById(id) {
    for (var i = 0; i < gallery.length; i++) if (String(gallery[i].id) === String(id)) return gallery[i];
    return null;
  }

  function galleryMatches(s) {
    for (var t = 0; t < 3; t++) if (gstate.tag[t] && s.tags[t] !== gstate.tag[t]) return false;
    if (gstate.mode === 'play' && !s.playMode) return false;
    if (gstate.mode === 'edit' && s.playMode) return false;
    if (state.search) {
      var words = state.search.toLowerCase().split(/\s+/);
      var text = (s.name + ' ' + s.owner).toLowerCase();
      for (var i = 0; i < words.length; i++) if (words[i] && text.indexOf(words[i]) < 0) return false;
    }
    return true;
  }

  function sideList(title, list, key) {
    var html = '<div class="section">' + title + '</div>';
    if (!list.length) html += '<a href="javascript:void 0" class="dim">Nothing yet</a>';
    for (var i = 0; i < list.length; i++) {
      var s = list[i];
      html += '<a href="javascript:void 0" data-pick="' + s.id + '" title="' + esc(s.name) + ' by ' + esc(s.owner) + '">'
        + (i + 1) + '. ' + esc(s.name || 'Unnamed build site') + '<span>' + (key === 'rating' ? s.rating : ago(s.listedAt)) + '</span></a>';
    }
    return html;
  }

  function ago(unix) {
    var d = Math.max(0, Math.floor(Date.now() / 1000) - unix);
    if (d < 3600) return Math.floor(d / 60) + 'm';
    if (d < 86400) return Math.floor(d / 3600) + 'h';
    return Math.floor(d / 86400) + 'd';
  }

  function tagSelect(index, label, list) {
    var html = '<select class="gsel" id="tag' + index + '"><option value="">' + label + '</option>';
    for (var i = 0; i < list.length; i++) {
      html += '<option value="' + esc(list[i]) + '"' + (gstate.tag[index] === list[i] ? ' selected' : '') + '>' + esc(list[i]) + '</option>';
    }
    return html + '</select>';
  }

  function renderGallery() {
    if (!gallery || !galleryTags) {
      $('side').innerHTML = '<div class="section">Gallery</div>';
      $('content').innerHTML = '<p class="note">Loading the gallery...</p>';
      loadGallery();
      return;
    }
    // Leaderboard (most liked) and rising stars (most recently listed): the two side lists of live's gallery.
    var byLikes = gallery.slice().sort(function (a, b) { return b.rating - a.rating || b.listedAt - a.listedAt; }).slice(0, LIST);
    var byListed = gallery.slice().sort(function (a, b) { return b.listedAt - a.listedAt; }).slice(0, LIST);
    $('side').innerHTML = '<div class="section">Gallery</div><a href="javascript:void 0" class="on" data-pick="">Build sites<span>'
      + gallery.length + '</span></a>' + sideList('Most liked', byLikes, 'rating') + sideList('Rising stars', byListed, 'listed');

    var html = '<h1 class="heading">Build sites</h1>'
      + '<p class="note">Build sites their owners published to the gallery (Build Site Management, U). Search by the three tags '
      + 'and by words in the site or owner name; Visit takes you there.</p>'
      + '<div class="filters">' + tagSelect(0, 'Any genre', galleryTags.genre) + tagSelect(1, 'Anything built', galleryTags.built)
      + tagSelect(2, 'Any activity', galleryTags.activity)
      + '<select class="gsel" id="mode"><option value="any"' + (gstate.mode === 'any' ? ' selected' : '') + '>Play or Edit Mode</option>'
      + '<option value="play"' + (gstate.mode === 'play' ? ' selected' : '') + '>In Play Mode</option>'
      + '<option value="edit"' + (gstate.mode === 'edit' ? ' selected' : '') + '>In Edit Mode</option></select>'
      + (gstate.tag[0] || gstate.tag[1] || gstate.tag[2] || gstate.mode !== 'any' || state.search ? '<button class="alt" id="gclear">Clear</button>' : '')
      + '</div>';
    var shown = 0;
    for (var i = 0; i < gallery.length; i++) {
      var s = gallery[i];
      if (!galleryMatches(s)) continue;
      shown++;
      html += siteCard(s);
    }
    if (!shown) html += '<p class="note">' + (gallery.length ? 'No build sites match.' : 'No build site is published yet.') + '</p>';
    $('content').innerHTML = html;
    if (gstate.open && $('site' + gstate.open)) $('site' + gstate.open).scrollIntoView();
  }

  function siteCard(s) {
    var open = String(gstate.open) === String(s.id);
    var tags = [];
    for (var t = 0; t < 3; t++) if (s.tags[t]) tags.push('<span class="tag">' + esc(s.tags[t]) + '</span>');
    var html = '<div class="site' + (open ? ' open' : '') + '" id="site' + s.id + '">'
      + (s.image ? '<img class="shot" src="' + esc(s.image) + '" alt="">' : '<div class="shot noshot"></div>')
      + '<div class="info"><div class="title">' + esc(s.name || 'Unnamed build site')
      + (s.playMode ? ' <span class="badge play">Play Mode</span>' : ' <span class="badge">Edit Mode</span>') + '</div>'
      + '<div class="meta"><span class="dot' + (s.online ? ' on' : '') + '"></span>' + esc(s.owner) + followLink(s.owner) + ' · ' + esc(s.landscape)
      + ' · listed ' + ago(s.listedAt) + ' ago' + (s.contests.length ? ' · in ' + esc(s.contests.join(', ')) : '') + '</div>'
      + '<div class="tags">' + tags.join('') + '</div>'
      + (s.description ? '<div class="desc">' + esc(s.description) + '</div>' : '') + '</div>'
      + '<div class="rating">' + s.rating + ' like' + (s.rating === 1 ? '' : 's') + '<br><a href="javascript:void 0" data-comments="' + s.id + '">'
      + s.comments + ' comment' + (s.comments === 1 ? '' : 's') + '</a></div>'
      + '<button data-visit="' + s.id + '">Visit</button></div>';
    if (open) html += commentsBox(s);
    return html;
  }

  function commentsBox(s) {
    var list = gstate.comments[s.id];
    var html = '<div class="comments" id="comments' + s.id + '">';
    if (!list) html += '<p class="note">Loading comments...</p>';
    else if (!list.length) html += '<p class="note">No comments yet. Be the first!</p>';
    for (var i = 0; list && i < list.length; i++) {
      var c = list[i];
      html += '<div class="comment"><b>' + esc(c.author) + '</b> <span class="when">' + ago(c.at) + ' ago</span>'
        + (c.removable ? ' <a href="javascript:void 0" class="del" data-uncomment="' + esc(c.id) + '" data-site="' + s.id + '">remove</a>' : '')
        + '<div>' + esc(c.text) + '</div></div>';
    }
    html += token ? '<div class="post"><input id="ctext' + s.id + '" maxlength="500" placeholder="Leave a comment about this site">'
      + '<button data-post="' + s.id + '">Post</button></div>' : '<p class="note">Open the Showcase from the game to comment.</p>';
    return html + '</div>';
  }

  function toggleComments(id) {
    gstate.open = String(gstate.open) === String(id) ? null : id;
    if (gstate.open && !gstate.comments[id]) loadComments(id);
    render();
  }

  function loadComments(id) {
    if (gstate.loadingComments[id]) return;
    gstate.loadingComments[id] = true;
    request('GET', '/api/gallery/comments', { site: id }, function (list) {
      gstate.loadingComments[id] = false;
      gstate.comments[id] = list || [];
      var s = gallery && siteById(id);
      if (s) s.comments = gstate.comments[id].length;
      if (state.view === 'gallery' && String(gstate.open) === String(id)) render();
    });
  }

  function postComment(id) {
    var box = $('ctext' + id);
    if (!box || !box.value.trim()) { toast('Write something first.', true); return; }
    request('POST', '/api/gallery/comment', { site: id, text: box.value.trim() }, function (r) {
      toast(r ? r.message : 'The server did not answer.', !(r && r.ok));
      if (r && r.ok) { delete gstate.comments[id]; loadComments(id); }
    });
  }

  function removeComment(id, site) {
    request('POST', '/api/gallery/uncomment', { id: id }, function (r) {
      toast(r ? r.message : 'The server did not answer.', !(r && r.ok));
      if (r && r.ok) { delete gstate.comments[site]; loadComments(site); }
    });
  }

  function visit(id) {
    if (!token) { toast('Open the Showcase from the game to sign in.', true); return; }
    request('POST', '/api/visit', { site: id }, function (r) {
      toast(r ? r.message : 'The server did not answer.', !(r && r.ok));
    });
  }

  // ---- Player Studio: designs other players submitted and peer review accepted ----
  function loadStudio() {
    request('GET', '/api/studio', null, function (list) {
      studio = list || [];
      if (state.view === 'studio') render();
    });
  }

  function renderStudio() {
    $('side').innerHTML = '<div class="section">Player Studio</div><a href="javascript:void 0" class="on">Designs<span>'
      + (studio ? studio.length : '') + '</span></a>';
    if (!studio) { $('content').innerHTML = '<p class="note">Loading Player Studio...</p>'; loadStudio(); return; }
    // price: what the buyer pays in Lumens (the creator's price at the store's rate; 0 = free, e.g. FreeStore on).
    var html = '<h1 class="heading">Player Studio designs</h1><p class="note">Designs made by players on this server and '
      + 'accepted by peer review. ' + (me.freeStore ? 'Get adds a copy to your Designs (Collected)'
        : 'Each costs its creator\'s price in Lumens, and the creator gets a share. Buy adds a copy to your Designs (Collected)')
      + '; it stays credited to its maker.</p>';
    var shown = 0;
    for (var i = 0; i < studio.length; i++) {
      var d = studio[i];
      var text = (d.name + ' ' + d.author + ' ' + d.tags + ' ' + d.description).toLowerCase();
      if (state.search && text.indexOf(state.search.toLowerCase()) < 0) continue;
      shown++;
      html += '<div class="site">' + (d.image ? '<img class="shot" src="' + esc(d.image) + '" alt="">' : '')
        + '<div class="info"><div class="title">' + esc(d.name || 'Unnamed design') + '</div>'
        + '<div class="meta">by ' + esc(d.author) + followLink(d.author) + ' · ' + d.voxels + ' voxels' + (d.tags ? ' · ' + esc(d.tags) : '') + '</div>'
        + (d.description ? '<div class="desc">' + esc(d.description) + '</div>' : '')
        + '<div class="meta">' + (d.price ? num(d.price) + ' Lumens' : 'Free') + '</div></div>'
        + '<button data-studio="' + esc(d.id) + '">' + (d.price ? 'Buy' : 'Get') + '</button></div>';
    }
    if (!shown) html += '<p class="note">No Player Studio designs' + (state.search ? ' match.' : ' yet.') + '</p>';
    $('content').innerHTML = html;
  }

  function takeDesign(id) {
    if (!token) { toast('Open the Showcase from the game to sign in.', true); return; }
    request('POST', '/api/studio/take', { product: id }, function (r) {
      toast(r ? r.message : 'The server did not answer.', !(r && r.ok));
      if (r && typeof r.lumens === 'number') { me.lumens = r.lumens; showWho(); }
    });
  }

  // ---- following (live: My Store, Start Following; Update Notes 27 Aug 2014; server: Game/ShowcaseFollow.cs) ----
  // Follow creators by character name and tags (one word, or several words = only items with all of them); once a day
  // the server mails what was listed on Player Studio and the Gallery since the last mail.
  var follows = null;
  function loadFollows() {
    request('GET', '/api/follows', null, function (r) {
      follows = r || { available: false, creators: [], tags: [] };
      if (state.view === 'following') renderFollowing();
    });
  }
  function followLink(name) {
    if (!name || name === '?' || !me.signedIn || name === me.name) return '';
    return ' <a href="javascript:void 0" class="follow" data-follow="' + esc(name) + '">Follow</a>';
  }
  function renderFollowing() {
    $('side').innerHTML = '<div class="section">Following</div><a href="javascript:void 0" class="on">Creators and tags<span>'
      + (follows ? follows.creators.length + follows.tags.length : '') + '</span></a>';
    if (!token) { $('content').innerHTML = '<p class="note">Open the Showcase from the game to sign in.</p>'; return; }
    if (!follows) { $('content').innerHTML = '<p class="note">Loading...</p>'; loadFollows(); return; }
    if (!follows.available) { $('content').innerHTML = '<p class="note">' + esc(follows.message || 'Following is not available on this server.') + '</p>'; follows = null; return; }
    var html = '<h1 class="heading">Following</h1><p class="note">Follow your favourite creators and the tags that interest you. '
      + (follows.digest ? 'Once a day you get an in-game mail listing what they put on Player Studio and the Gallery since the last one '
        + '(no mail when there is nothing new).' : 'The daily mail is switched off on this server.')
      + ' Tags: one word follows everything tagged with it; several words in one entry (e.g. <i>castle pvp</i>) follow only items '
      + 'tagged with all of them.</p>';
    html += '<div class="vendor"><h2>Creators</h2><div class="row"><input id="fcreator" placeholder="Character name"> '
      + '<button id="fcreatorgo">Start Following</button></div>';
    if (!follows.creators.length) html += '<p class="note">You follow no creators yet.</p>';
    for (var i = 0; i < follows.creators.length; i++) {
      var c = follows.creators[i];
      html += '<div class="row"><div class="nm">' + esc(c.name || ('#' + c.id)) + '</div>'
        + '<button class="alt" data-unfollow="' + esc(c.id) + '">Stop following</button></div>';
    }
    html += '</div><div class="vendor"><h2>Tags</h2><div class="row"><input id="ftags" placeholder="Tag words"> '
      + '<button id="ftagsgo">Start Following</button></div>';
    if (!follows.tags.length) html += '<p class="note">You follow no tags yet.</p>';
    for (var j = 0; j < follows.tags.length; j++) {
      html += '<div class="row"><div class="nm">' + esc(follows.tags[j]) + '</div>'
        + '<button class="alt" data-untag="' + esc(follows.tags[j]) + '">Stop following</button></div>';
    }
    $('content').innerHTML = html + '</div>';
  }
  function afterFollow(r) {
    toast(r ? r.message : 'The server did not answer.', !(r && r.ok));
    if (r && r.ok) { follows = null; if (state.view === 'following') renderFollowing(); else loadFollows(); }
  }
  function follow(data) {
    if (!token) { toast('Open the Showcase from the game to sign in.', true); return; }
    request('POST', '/api/follow', data, afterFollow);
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
      + '<h2>Showcase</h2><ul><li><b>Store</b>: ' + (me.freeStore ? 'outfits, decorations and designs, all free. Get puts the item in your bag.'
        : 'what the live Marketplace sold (Build Site Expansions, the Fantasy Parlor Pack, pets, Reward Chests), for Lumens. '
        + 'Buy puts the item in your bag. Lumens are earned in game, never bought.') + '</li>'
      + '<li><b>Gallery</b>: build sites their owners published (Build Site Management, U: choose the three tags, then '
      + 'Publish to Gallery). Search by tag and name, filter by Play Mode, like them from their banner, leave comments; '
      + 'Visit takes you there. Only listed sites can be liked or entered into a competition.</li>'
      + '<li><b>Player Studio</b>: designs other players submitted (Designs window, Player Studio) and peer review '
      + 'accepted. ' + (me.freeStore ? 'Get copies one into your Designs.' : 'Buy copies one into your Designs for its creator\'s price '
        + 'in Lumens; the creator is mailed a share.') + '</li>'
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
    while (el && el !== document && !(el.getAttribute && (el.getAttribute('data-take') || el.getAttribute('data-visit') || el.getAttribute('data-studio')
      || el.getAttribute('data-list') || el.getAttribute('data-price') || el.getAttribute('data-unlist')
      || el.getAttribute('data-comments') || el.getAttribute('data-post') || el.getAttribute('data-uncomment') || el.getAttribute('data-pick') !== null
      || el.getAttribute('data-follow') || el.getAttribute('data-unfollow') || el.getAttribute('data-untag') || el.id === 'fcreatorgo' || el.id === 'ftagsgo'
      || el.getAttribute('data-item') || el.getAttribute('data-group') !== null || el.id === 'more' || el.id === 'close' || el.id === 'gclear'))) {
      el = el.parentNode;
    }
    if (!el || el === document) return;
    if (el.getAttribute('data-take')) { take(+el.getAttribute('data-take')); return; }
    if (el.getAttribute('data-visit')) { visit(el.getAttribute('data-visit')); return; }
    if (el.getAttribute('data-comments')) { toggleComments(el.getAttribute('data-comments')); return; }
    if (el.getAttribute('data-post')) { postComment(el.getAttribute('data-post')); return; }
    if (el.getAttribute('data-uncomment')) { removeComment(el.getAttribute('data-uncomment'), el.getAttribute('data-site')); return; }
    if (el.getAttribute('data-pick') !== null) {
      // A side list entry: clear the filters so the site is on screen, open its comments and scroll to it.
      var pick = el.getAttribute('data-pick');
      gstate.tag = ['', '', '']; gstate.mode = 'any'; state.search = ''; $('search').value = '';
      gstate.open = pick || null;
      if (pick && !gstate.comments[pick]) loadComments(pick);
      render();
      return;
    }
    if (el.id === 'gclear') { gstate.tag = ['', '', '']; gstate.mode = 'any'; state.search = ''; $('search').value = ''; render(); return; }
    if (el.getAttribute('data-studio')) { takeDesign(el.getAttribute('data-studio')); return; }
    if (el.getAttribute('data-follow')) { follow({ creator: el.getAttribute('data-follow') }); return; }
    if (el.id === 'fcreatorgo') { follow({ creator: $('fcreator').value }); return; }
    if (el.id === 'ftagsgo') { follow({ tags: $('ftags').value }); return; }
    if (el.getAttribute('data-unfollow')) { request('POST', '/api/unfollow', { creator: el.getAttribute('data-unfollow') }, afterFollow); return; }
    if (el.getAttribute('data-untag')) { request('POST', '/api/unfollow', { tags: el.getAttribute('data-untag') }, afterFollow); return; }
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

  document.addEventListener('change', function (e) {
    var el = e.target;
    if (!el || !el.id) return;
    if (el.id === 'tag0' || el.id === 'tag1' || el.id === 'tag2') { gstate.tag[+el.id.charAt(3)] = el.value; render(); }
    else if (el.id === 'mode') { gstate.mode = el.value; render(); }
  });
  document.addEventListener('keydown', function (e) {
    if (e.keyCode === 13 && e.target && e.target.id && e.target.id.indexOf('ctext') === 0) postComment(e.target.id.substring(5));
    if (e.keyCode === 13 && e.target && e.target.id === 'fcreator') follow({ creator: e.target.value });
    if (e.keyCode === 13 && e.target && e.target.id === 'ftags') follow({ tags: e.target.value });
  });

  var searchTimer = null;
  $('search').addEventListener('input', function () {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(function () { state.search = $('search').value.trim(); render(); }, 200);
  });
  document.addEventListener('keydown', function (e) { if (e.keyCode === 27) closeDetail(); });
  window.addEventListener('hashchange', route);

  request('GET', '/api/me', null, function (r) {
    if (r) me = r;
    // Vendor shops off (the server's VENDOR_SHOPS, as live: the booth is decorative): no My vendors tab.
    $('tab-myvendors').style.display = me.vendorShops ? '' : 'none';
    showWho();
    render();
  });
  loadCatalogue();
  route();
})();
