// LaunchPad page: talks to the native side (Bridge.cs) with JSON messages, as Landmark's LaunchPad page drove its shell
// and patcher plug-in. call(method, args) -> Promise; events: progress, gameExited.
(function () {
  'use strict';
  var host = window.chrome && window.chrome.webview;
  var pending = {}, nextId = 1, state = null, info = null, patch = null;
  var loginMode = 'login', busy = false, longJob = false, packPath = null;
  var $ = function (id) { return document.getElementById(id); };

  // files: File objects (from a drop) that go with the message; the native side gets their paths.
  function call(method, args, files) {
    return new Promise(function (resolve, reject) {
      if (!host) { reject(new Error('Open this page with LaunchPad.exe.')); return; }
      var id = String(nextId++);
      pending[id] = { resolve: resolve, reject: reject };
      var message = { id: id, method: method, args: args || {} };
      if (files) host.postMessageWithAdditionalObjects(message, files); else host.postMessage(message);
    });
  }

  if (host) host.addEventListener('message', function (e) {
    var m = e.data;
    if (m && m.event) { onEvent(m.event, m.data); return; }
    var p = m && pending[m.id];
    if (!p) return;
    delete pending[m.id];
    if (m.ok) p.resolve(m.result); else p.reject(new Error(m.error || 'Failed.'));
  });

  function el(tag, text, cls) { var e = document.createElement(tag); if (text != null) e.textContent = text; if (cls) e.className = cls; return e; }
  function selected() { return state && state.servers.filter(function (s) { return s.id === state.selected; })[0]; }
  function fmtBytes(n) {
    if (n >= 1e9) return (n / 1e9).toFixed(2) + ' GB';
    if (n >= 1e6) return (n / 1e6).toFixed(1) + ' MB';
    return Math.ceil(n / 1e3) + ' KB';
  }

  // ---- modals
  function openModal(id) { $('modalMask').classList.remove('hidden'); $(id).classList.remove('hidden'); }
  function closeModals() {
    ['loginModal', 'errorModal', 'spinnerModal', 'patchModal'].forEach(function (id) { $(id).classList.add('hidden'); });
    $('modalMask').classList.add('hidden');
  }
  function showError(text, title) {
    closeModals();
    $('errorTitle').textContent = title || 'Something went wrong';
    $('errorText').textContent = text;
    openModal('errorModal');
  }
  document.querySelectorAll('.modal-close').forEach(function (b) { b.addEventListener('click', closeModals); });

  // ---- state
  function apply(s) {
    if (!s) return;
    state = s;
    $('buildLabel').textContent = s.folderOk ? (s.build ? 'v' + s.build : 'unknown build') : 'not in the Landmark folder';
    var sel = $('serverSelect');
    sel.innerHTML = '';
    if (!s.servers.length) sel.appendChild(el('option', 'Add a server...'));
    s.servers.forEach(function (srv) {
      var o = el('option', srv.name); o.value = srv.id; if (srv.id === s.selected) o.selected = true; sel.appendChild(o);
    });
    var srv = selected();
    $('accountButton').textContent = srv && srv.loggedIn ? srv.account + ' ▾' : 'Log in ▾';
    $('menuLogout').classList.toggle('hidden', !(srv && srv.loggedIn));
    $('autoPlay').checked = s.autoPlay;
    $('optAutoPlay').checked = s.autoPlay;
    $('optLocale').value = s.locale || '';
    $('optServerUpdates').checked = !!s.allowServerUpdates;
    $('optFebruary').checked = !!s.allowFebruaryWeak;
    document.querySelectorAll('.steam-build').forEach(function (e) { e.textContent = s.steamBuild; });
    if (!s.ownSteamCopy && $('makeSteam').value === 'own') $('makeSteam').value = '';
    $('makeSteam').options[0].disabled = !s.ownSteamCopy;
    $('folderPath').textContent = s.folder;
    $('folderBuild').textContent = s.folderOk ? 'Game build: ' + (s.build || 'unknown') : 'LaunchPad.exe is not next to Landmark64.exe. Move it into your Landmark folder.';
    $('steamBuild').textContent = s.steamBuild;
    $('cacheInfo').textContent = s.cacheBytes ? 'Downloaded patches kept for later: ' + fmtBytes(s.cacheBytes) + '.' : '';
    $('clearCache').classList.toggle('hidden', !s.cacheBytes);
    renderServerList();
    renderPacks();
    updateAction();
  }

  function renderServerList() {
    var list = $('serverList'); list.innerHTML = '';
    state.servers.forEach(function (srv) {
      var li = el('li'); if (srv.id === state.selected) li.className = 'selected';
      li.appendChild(el('span', srv.name));
      li.appendChild(el('span', srv.address + (srv.account ? ' · ' + srv.account : ''), 'meta'));
      li.addEventListener('click', function () { fillServerForm(srv); call('selectServer', { server: srv.id }).then(apply).then(loadInfo); });
      list.appendChild(li);
    });
    if (!state.servers.length) list.appendChild(el('li', 'No servers yet: add one below.', 'meta'));
    fillServerForm(selected());
  }

  function fillServerForm(srv) {
    $('srvId').value = srv ? srv.id : '';
    $('srvName').value = srv && srv.name !== srv.address ? srv.name : '';
    $('srvAddress').value = srv ? srv.address : '';
    $('srvRemove').disabled = !srv;
  }

  function renderPacks() {
    var list = $('packList'); list.innerHTML = '';
    state.packs.slice().reverse().forEach(function (p) {
      var li = el('li');
      var name = el('span', p.name + ' ');
      name.appendChild(el('span', 'verified', 'badge official'));
      li.appendChild(name);
      li.appendChild(el('span', (p.source ? 'from ' + p.source : 'yours') + ' · ' + (p.build || p.version || '') + ' · ' +
        p.files + ' files · backup ' + fmtBytes(p.backupBytes) + ' · ' + p.installedAt, 'meta'));
      list.appendChild(li);
    });
    if (!state.packs.length) list.appendChild(el('li', 'None: this is the Steam game.', 'meta'));
    $('undoPack').disabled = $('revertAll').disabled = !state.packs.length || busy;
  }

  // The server hosts patches the game doesn't have yet (UPDATE); or it needs another build and hosts none (PATCH: the
  // player installs a pack).
  function needsUpdate() { return !!(patch && patch.needed); }
  function needsPatch() {
    return !needsUpdate() && !!(info && info.requiredClientBuild && state && state.build && info.requiredClientBuild !== state.build);
  }

  function updateAction() {
    var btn = $('actionBtn'), srv = selected();
    btn.disabled = busy && !longJob;
    btn.classList.remove('secondary', 'stop');
    if (longJob) { btn.textContent = 'STOP'; btn.classList.add('stop'); return; }
    if (!state || !state.folderOk) { btn.textContent = 'MOVE ME'; btn.disabled = true; return; }
    if (!srv) { btn.textContent = 'ADD SERVER'; btn.classList.add('secondary'); return; }
    if (needsUpdate()) { btn.textContent = 'UPDATE'; btn.classList.add('secondary'); return; }
    if (needsPatch()) { btn.textContent = 'PATCH'; btn.classList.add('secondary'); return; }
    btn.textContent = srv.loggedIn ? 'PLAY' : 'LOG IN';
    if (!srv.loggedIn) btn.classList.add('secondary');
  }

  // ---- server info (banner, worlds, news, patches)
  function loadInfo() {
    var srv = selected();
    info = null; patch = null;
    $('worlds').innerHTML = '';
    $('websiteLink').classList.add('hidden');
    if (!srv) {
      $('serverName').textContent = 'No server yet';
      $('serverStatus').textContent = 'Add your server in Settings to begin.';
      $('lpNewsContent').innerHTML = '<p class="muted">No news.</p>';
      updateAction();
      return Promise.resolve();
    }
    $('serverName').textContent = srv.name;
    $('serverStatus').textContent = 'Contacting ' + srv.address + '...';
    return call('serverInfo', { server: srv.id }).then(function (r) {
      var i = r.info;
      info = i; patch = r.patch;
      $('serverName').textContent = i.name || srv.name;
      var up = (i.worlds || []).filter(function (w) { return w.up; }).length;
      var status = up + ' of ' + (i.worlds || []).length + ' worlds online';
      if (i.requiredClientBuild) status += ' · needs game build ' + i.requiredClientBuild;
      if (needsUpdate()) status += ' · has an update for your game';
      else if (needsPatch()) status += ' (you have ' + state.build + ': install its patch pack)';
      $('serverStatus').textContent = status;
      (i.worlds || []).forEach(function (w) {
        var li = el('li');
        li.appendChild(el('div', w.name));
        li.appendChild(el('div', w.up ? '● online · ' + w.players + ' playing' : '● offline', w.up ? 'up' : 'down'));
        $('worlds').appendChild(li);
      });
      if (i.websiteUrl) $('websiteLink').classList.remove('hidden');
      var news = $('lpNewsContent'); news.innerHTML = '';
      (i.news || []).forEach(function (n) {
        var d = el('div', null, 'news-item');
        d.appendChild(el('h3', n.title || ''));
        if (n.date) d.appendChild(el('div', n.date, 'date'));
        d.appendChild(el('p', n.text || ''));
        news.appendChild(d);
      });
      if (!(i.news || []).length) news.innerHTML = '<p class="muted">No news.</p>';
      updateAction();
    }).catch(function (e) {
      $('serverStatus').textContent = e.message;
      updateAction();
    });
  }

  // ---- progress events
  var phases = {
    checking: 'Checking the pack', verifying: 'Checking files', downloading: 'Downloading', making: 'Making the pack',
    'backing up': 'Backing up', installing: 'Installing', restoring: 'Restoring', validating: 'Validating'
  };
  function onEvent(name, data) {
    if (name === 'progress') {
      if (data.phase === 'idle') return;      // job() resets the bar when the answer comes
      var pct = data.total ? Math.floor(data.done * 100 / data.total) : 0;
      $('progressFill').className = data.phase === 'downloading' || data.phase === 'installing' ? '' : 'checking';
      setProgress(pct, (phases[data.phase] || data.phase) + ' ' + pct + '% · ' + fmtBytes(data.done) + ' of ' +
        fmtBytes(data.total) + ' · ' + data.file);
    } else if (name === 'gameExited') {
      setProgress(0, 'Ready.');
      if (data && data.crashed)
        showError('Landmark closed unexpectedly (exit code ' + data.hex + '). Press PLAY to go back in. ' +
          'If it keeps happening, its logs are in the Logs folder (Settings > Game folder > Open the folder).', 'Landmark crashed');
      loadInfo();
    }
  }
  function setProgress(pct, text) { $('progressFill').style.width = pct + '%'; $('progressText').textContent = text; }

  function setBusy(on, long) {
    busy = on; longJob = !!(on && long);
    if (!state) return;
    updateAction();
    $('undoPack').disabled = $('revertAll').disabled = !state.packs.length || on;
  }
  // A long job (verify, update, install, undo, validate): the action button stops it meanwhile.
  function job(method, args) {
    setBusy(true, true);
    var done = function () { setBusy(false); $('progressFill').className = ''; setProgress(0, 'Ready.'); };
    return call(method, args).then(function (r) { done(); return r; }, function (e) { done(); throw e; });
  }

  // ---- header
  $('dragArea').addEventListener('mousedown', function (e) { if (e.button === 0) call('drag'); });
  $('btnMinimize').addEventListener('click', function () { call('minimize'); });
  $('btnClose').addEventListener('click', function () { call('close'); });
  $('serverSelect').addEventListener('change', function () { call('selectServer', { server: this.value }).then(apply).then(loadInfo); });
  $('accountButton').addEventListener('click', function (e) { e.stopPropagation(); $('accountMenu').classList.toggle('hidden'); });
  document.addEventListener('click', function () { $('accountMenu').classList.add('hidden'); });
  $('menuLogin').addEventListener('click', function () { openLogin('login'); });
  $('menuCreate').addEventListener('click', function () { openLogin('create'); });
  $('menuLogout').addEventListener('click', function () { var s = selected(); if (s) call('logout', { server: s.id }).then(apply); });
  $('openWebsite').addEventListener('click', function () { if (info && info.websiteUrl) call('openUrl', { url: info.websiteUrl }); });

  // ---- login / create account
  function openLogin(mode) {
    var srv = selected();
    if (!srv) { openSettings('servers'); return; }
    loginMode = mode;
    var create = mode === 'create';
    $('loginTitle').textContent = create ? 'Create an account' : 'Log in';
    $('loginServer').textContent = 'on ' + srv.name + ' (' + srv.address + ')';
    $('confirmRow').classList.toggle('hidden', !create);
    $('loginSubmit').textContent = create ? 'Create account' : 'Log in';
    $('loginSwitch').textContent = create ? 'I already have an account' : 'Create a new account';
    $('loginError').textContent = '';
    $('loginAccount').value = srv.account || '';
    $('loginPassword').value = ''; $('loginConfirm').value = '';
    closeModals(); openModal('loginModal');
    (srv.account ? $('loginPassword') : $('loginAccount')).focus();
  }
  $('loginSwitch').addEventListener('click', function () { openLogin(loginMode === 'create' ? 'login' : 'create'); });
  $('loginForm').addEventListener('submit', function (e) {
    e.preventDefault();
    var srv = selected(), create = loginMode === 'create';
    var account = $('loginAccount').value.trim(), password = $('loginPassword').value;
    if (create && password !== $('loginConfirm').value) { $('loginError').textContent = 'The two passwords are different.'; return; }
    if (create && info && info.registrationOpen === false) { $('loginError').textContent = 'This server is not taking new accounts.'; return; }
    $('loginSubmit').disabled = true;
    $('loginError').textContent = create ? 'Creating your account...' : 'Logging in...';
    call(create ? 'register' : 'login', { server: srv.id, account: account, password: password, remember: $('loginRemember').checked })
      .then(function (s) { closeModals(); apply(s); })
      .catch(function (err) { $('loginError').textContent = err.message; })
      .then(function () { $('loginSubmit').disabled = false; $('loginPassword').value = ''; $('loginConfirm').value = ''; });
  });

  // ---- action button
  $('actionBtn').addEventListener('click', function () {
    if (longJob) { call('cancel'); return; }
    var srv = selected();
    if (!srv) { openSettings('servers'); return; }
    if (needsUpdate()) { askUpdate(true); return; }
    if (needsPatch()) { openSettings('packs'); return; }
    if (!srv.loggedIn) { openLogin('login'); return; }
    play();
  });

  // The server's update: every time, LaunchPad first fetches and checks only its list (updateOffer), shows the player what
  // it is, and downloads nothing until they click.
  function askUpdate(thenPlay) {
    var srv = selected();
    setProgress(0, 'Checking ' + srv.name + '\'s update...');
    job('updateOffer', { server: srv.id }).then(function (o) {
      setProgress(0, 'Ready.');
      $('patchServer').textContent = o.server + ' (' + o.address + ')';
      var list = $('patchList'); list.innerHTML = '';
      list.appendChild(el('li', 'Leads to: ' + o.build + ' (' + o.name + ')'));
      list.appendChild(el('li', 'Download: ' + fmtBytes(o.packBytes) + ' (' + o.files + ' files, ' + fmtBytes(o.bytes) +
        ' once installed' + (o.deletes ? '; removes ' + o.deletes + ' old file(s)' : '') + ')'));
      list.appendChild(el('li', 'Its list matches LaunchPad\'s own list of the genuine files. The files themselves are checked after the download.'));
      closeModals(); openModal('patchModal');
      $('patchAccept').onclick = function () { closeModals(); runUpdate(o.id, thenPlay); };
    }).catch(function (e) { setProgress(0, ''); showError(e.message, 'This server\'s update was refused'); });
  }
  function runUpdate(id, thenPlay) {
    var srv = selected();
    job('update', { server: srv.id, id: id }).then(function (s) {
      apply(s);
      setProgress(100, 'Your game is up to date for ' + srv.name + '.');
      return loadInfo();
    }).then(function () {
      var now = selected();
      if (thenPlay && now && now.loggedIn && !needsUpdate() && !needsPatch()) play();
    }).catch(function (e) { setProgress(0, ''); showError(e.message, 'The update did not finish'); });
  }

  function play() {
    var srv = selected();
    $('spinnerText').textContent = 'Starting Landmark...';
    openModal('spinnerModal');
    setBusy(true);
    call('play', { server: srv.id }).then(function () {
      setProgress(100, 'Landmark is running.');
      setTimeout(closeModals, 1500);
    }).catch(function (e) {
      showError(e.message, 'Could not start the game');
    }).then(function () { setBusy(false); });
  }

  // ---- settings
  function openSettings(tab) {
    $('lpSettings').classList.remove('hidden');
    document.querySelectorAll('#settingsMenu button').forEach(function (b) { b.classList.toggle('active', b.dataset.tab === tab); });
    document.querySelectorAll('.tab').forEach(function (t) { t.classList.toggle('hidden', t.id !== 'tab-' + tab); });
  }
  $('settingsButton').addEventListener('click', function () {
    if ($('lpSettings').classList.contains('hidden')) openSettings('servers'); else $('lpSettings').classList.add('hidden');
  });
  $('settingsClose').addEventListener('click', function () { $('lpSettings').classList.add('hidden'); });
  document.querySelectorAll('#settingsMenu button').forEach(function (b) { b.addEventListener('click', function () { openSettings(b.dataset.tab); }); });

  $('serverForm').addEventListener('submit', function (e) {
    e.preventDefault();
    call('saveServer', { server: $('srvId').value, name: $('srvName').value, address: $('srvAddress').value })
      .then(function (s) { apply(s); loadInfo(); }).catch(function (err) { showError(err.message); });
  });
  $('srvNew').addEventListener('click', function () { fillServerForm(null); $('srvAddress').focus(); });
  $('srvRemove').addEventListener('click', function () {
    var id = $('srvId').value; if (!id) return;
    call('removeServer', { server: id }).then(function (s) { apply(s); loadInfo(); });
  });

  // ---- the player's own packs
  function showPack(r) {
    if (!r) return;
    openSettings('packs');
    packPath = r.path;
    var i = r.info, box = $('packPreview');
    box.innerHTML = '';
    var title = el('h3', i.name + ' ');
    title.appendChild(el('span', i.verified ? 'verified' : 'refused', 'badge ' + (i.verified ? 'official' : 'refused')));
    box.appendChild(title);
    if (i.description) box.appendChild(el('p', i.description, 'muted'));
    box.appendChild(el('p', i.files + ' files, ' + fmtBytes(i.bytes) + (i.deletes ? '; removes ' + i.deletes + ' old file(s)' : '') + '.', 'muted'));
    if (i.verified && !(i.weak && i.weak.length)) box.appendChild(el('p', 'Update to ' + i.build + ': every file matches that build exactly.'));
    if (i.verified && i.weak && i.weak.length) box.appendChild(el('p', 'Warning: update to ' + i.build + '. These files are only checked by ' +
      'size and CRC32 (no SHA-256 of the genuine file is known), so a forged file could pass: ' + i.weak.join(', ') +
      '. Every other file matches the build exactly. Only install it if you trust where the pack came from.', 'error'));
    else box.appendChild(el('p', 'This is not the verified update to the final build, so it can\'t be installed: ' + i.problems.join('; ') + '.', 'error'));
    if (i.verified && !i.buildOk) box.appendChild(el('p', 'Your game is ' + (i.currentBuild || 'an unknown build') + '; the update goes on the Steam build ' + state.steamBuild + '.', 'error'));
    var buttons = el('div', null, 'buttons');
    var go = el('button', 'Install', 'primary'); go.disabled = !i.verified || !i.buildOk || busy;
    var cancel = el('button', 'Cancel');
    buttons.appendChild(go); buttons.appendChild(cancel); box.appendChild(buttons);
    box.classList.remove('hidden');
    cancel.addEventListener('click', function () { box.classList.add('hidden'); packPath = null; });
    go.addEventListener('click', function () {
      go.disabled = true;
      job('installPack', { path: packPath }).then(function (s) {
        box.classList.add('hidden'); apply(s); loadInfo();
        setProgress(100, 'Patch pack installed.');
      }).catch(function (err) { setProgress(0, ''); showError(err.message, 'The pack was not installed'); });
    });
  }
  $('choosePack').addEventListener('click', function () {
    call('choosePack').then(showPack).catch(function (err) { showError(err.message, 'Not a patch pack'); });
  });
  // Dropping the pack (.lmpatch) anywhere on the window previews it.
  document.addEventListener('dragover', function (e) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; });
  document.addEventListener('drop', function (e) {
    e.preventDefault();
    var files = e.dataTransfer && e.dataTransfer.files;
    if (!files || !files.length || busy) return;
    call('dropPack', {}, [files[0]]).then(showPack).catch(function (err) { showError(err.message, 'Not a patch pack'); });
  });
  $('undoPack').addEventListener('click', function () {
    job('undoPack').then(function (s) { apply(s); loadInfo(); setProgress(0, 'The last patch was undone.'); })
      .catch(function (err) { showError(err.message); });
  });
  $('revertAll').addEventListener('click', function () {
    job('revertAll').then(function (s) { apply(s); loadInfo(); setProgress(0, 'Your game is the Steam game again.'); })
      .catch(function (err) { showError(err.message); });
  });
  $('clearCache').addEventListener('click', function () { call('clearCache').then(apply); });

  // ---- checks
  function problemList(out, r, okText, badText) {
    out.innerHTML = '';
    out.appendChild(el('p', r.total === 0 ? okText : badText, r.total === 0 ? '' : 'error'));
    if (!r.total) return;
    var list = el('div', null, 'problems');
    r.problems.forEach(function (p) { list.appendChild(el('div', p.path + ': ' + p.issue)); });
    if (r.total > r.problems.length) list.appendChild(el('div', '... and ' + (r.total - r.problems.length) + ' more'));
    out.appendChild(list);
  }
  $('validate').addEventListener('click', function () {
    var out = $('validateResult'); out.innerHTML = '';
    job('validate').then(function (r) {
      problemList(out, r, 'All ' + r.checkedFiles + ' files are as they should be.', r.total + ' of ' + r.checkedFiles + ' files differ:');
    }).catch(function (err) { out.innerHTML = ''; out.appendChild(el('p', err.message, 'error')); });
  });
  $('verifySteam').addEventListener('click', function () {
    var out = $('verifyResult'); out.innerHTML = '';
    job('verifySteam').then(function (r) {
      problemList(out, r, 'This is a complete Steam copy of Landmark ' + r.steamBuild + '.',
        r.total + ' file(s) are missing or changed, so patches can\'t be installed until Steam repairs them:');
    }).catch(function (err) { out.innerHTML = ''; out.appendChild(el('p', err.message, 'error')); });
  });
  $('openFolder').addEventListener('click', function () { call('openFolder'); });
  $('autoPlay').addEventListener('change', function () { call('setOption', { autoPlay: this.checked }).then(apply); });
  $('optAutoPlay').addEventListener('change', function () { call('setOption', { autoPlay: this.checked }).then(apply); });
  $('optLocale').addEventListener('change', function () { call('setOption', { locale: this.value }).then(apply); });
  $('optFebruary').addEventListener('change', function () {
    call('setOption', { allowFebruaryWeak: this.checked }).then(apply);
    $('packPreview').classList.add('hidden');
  });
  $('optServerUpdates').addEventListener('change', function () {
    call('setOption', { allowServerUpdates: this.checked }).then(apply).then(loadInfo);
  });

  // ---- making the update pack from the player's own 0.695 copy
  var make = { from: '', steamPath: '', out: '' };
  function makeArgs() {
    var steam = $('makeSteam').value;
    return { from: make.from, steam: steam === 'other' ? make.steamPath : steam, out: make.out };
  }
  $('makeChooseFrom').addEventListener('click', function () {
    call('chooseFolder', { title: 'Choose your 0.695 Landmark folder' }).then(function (p) {
      if (p) { make.from = p; $('makeFrom').textContent = p; $('makeResult').innerHTML = ''; }
    });
  });
  $('makeSteam').addEventListener('change', function () {
    if (this.value !== 'other') { $('makeSteamPath').textContent = ''; return; }
    call('chooseFolder', { title: 'Choose a Steam copy of Landmark ' + state.steamBuild }).then(function (p) {
      if (p) { make.steamPath = p; $('makeSteamPath').textContent = p; } else { $('makeSteam').value = ''; }
    });
  });
  $('makeChooseOut').addEventListener('click', function () {
    call('chooseSave').then(function (p) { if (p) { make.out = p; $('makeOut').textContent = p; } });
  });
  function folderResult(out, r) {
    out.innerHTML = '';
    if (r.ok && r.weak && r.weak.length) { out.appendChild(el('p', 'This is the ' + r.build + '. Warning: ' + r.weak.join(', ') +
      ' are only checked by size and CRC32 (your February setting); every other file of the update is genuine (SHA-256).', 'error')); return; }
    if (r.ok) { out.appendChild(el('p', 'This is the ' + r.build + ': all ' + r.checkedFiles + ' files of the update are genuine.')); return; }
    problemList(out, r, '', 'Not a known 0.695 build file for file: ' + r.total + ' file(s) are missing, different or can\'t be verified (closest: ' + r.closest + '):');
  }
  $('makeCheck').addEventListener('click', function () {
    var out = $('makeResult'); out.innerHTML = '';
    job('checkMakeSource', { from: make.from }).then(function (r) { folderResult(out, r); })
      .catch(function (err) { out.innerHTML = ''; out.appendChild(el('p', err.message, 'error')); });
  });
  $('makeGo').addEventListener('click', function () {
    var out = $('makeResult'); out.innerHTML = '';
    job('makePack', makeArgs()).then(function (r) {
      out.appendChild(el('p', 'Done: ' + r.path + ' (' + fmtBytes(r.bytes) + '), the update to ' + r.build + ': ' + r.edits +
        ' in-place edits, ' + r.patches + ' patches, ' + r.whole + ' whole files. It was checked again with the rules a server ' +
        'and every player use' + (r.replayed ? ' (' + r.replayed + ' edits and patches replayed on the Steam copy)' : '') + '.'));
      setProgress(100, 'Update pack made.');
    }).catch(function (err) { out.innerHTML = ''; out.appendChild(el('p', err.message, 'error')); });
  });

  // ---- start
  call('state').then(function (s) {
    apply(s);
    if (s.notice) showError(s.notice, 'Update undone');
    if (!s.folderOk) {
      showError('LaunchPad.exe must be in your Landmark folder, next to Landmark64.exe. It is in ' + s.folder + '.', 'Wrong folder');
      return;
    }
    if (!s.servers.length) { loadInfo(); openSettings('servers'); return; }
    loadInfo().then(function () {
      var srv = selected();
      if (!s.autoPlay || !srv || !srv.loggedIn) return;
      // A server's update is never started without the player's click.
      if (!needsUpdate() && !needsPatch()) play();
    });
  }).catch(function (e) { showError(e.message); });
})();
