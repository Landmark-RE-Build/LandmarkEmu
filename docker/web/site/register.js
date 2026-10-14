// Account form: checks the fields, derives the SRP salt and verifier in the browser (srp.js) and posts only those to
// /api/register (nginx passes it to the auth server's /srp/register, rate-limited).
(function () {
  'use strict';
  var form = document.getElementById('register');
  var status = document.getElementById('status');
  var button = document.getElementById('submit');
  var started = Date.now();

  function show(text, kind) {
    status.textContent = text;
    status.className = kind || '';
  }

  form.addEventListener('submit', function (event) {
    event.preventDefault();
    var username = document.getElementById('username').value.trim();
    var password = document.getElementById('password').value;
    var confirm = document.getElementById('confirm').value;

    if (document.getElementById('website').value !== '' || Date.now() - started < 2000) {
      show('Please try again.', 'error');
      return;
    }
    if (!/^[A-Za-z0-9_]{3,32}$/.test(username)) {
      show('Account name: 3 to 32 letters, digits or _.', 'error');
      return;
    }
    if (password.length < 6) {
      show('The password needs at least 6 characters.', 'error');
      return;
    }
    if (password !== confirm) {
      show('The two passwords are different.', 'error');
      return;
    }

    button.disabled = true;
    show('Creating your account...');
    // Let the message paint before the key derivation (about a quarter of a second).
    setTimeout(function () {
      var salt, verifier;
      try {
        salt = LandmarkSrp.newSalt();
        verifier = LandmarkSrp.makeVerifier(password, salt);
      } catch (e) {
        button.disabled = false;
        show('Your browser could not prepare the account. Try another browser.', 'error');
        return;
      }
      var query = 'username=' + encodeURIComponent(username) + '&salt=' + salt + '&verifier=' + verifier;
      fetch('api/register?' + query, { method: 'POST', cache: 'no-store' })
        .then(function (response) {
          return response.json().catch(function () { return {}; }).then(function (body) {
            return { status: response.status, message: body.message || body.Message || '' };
          });
        })
        .then(function (r) {
          button.disabled = false;
          if (r.status === 200) {
            form.reset();
            show('Account "' + username + '" created. Sign in with it in the launcher.', 'ok');
          } else if (r.status === 409) {
            show('That account name is taken. Pick another.', 'error');
          } else if (r.status === 429) {
            show(r.message || 'Too many attempts. Wait a while and try again.', 'error');
          } else {
            show(r.message || 'The account could not be created (error ' + r.status + ').', 'error');
          }
        })
        .catch(function () {
          button.disabled = false;
          show('The server did not answer. Try again later.', 'error');
        });
    }, 30);
  });
})();
