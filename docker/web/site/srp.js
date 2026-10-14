// SRP-6a account registration, the browser side of LandmarkEmulator.Shared/Network/Cryptography/Srp.cs:
// x = PBKDF2-HMAC-SHA256(password UTF-8, salt bytes, 10000 iterations, 32 bytes), verifier v = 2^x mod N (RFC 5054
// 2048-bit group), sent as 512 hex digits with a random 16-byte salt. The password itself never leaves the browser.
// SHA-256 and PBKDF2 are written out here because the browser's crypto.subtle only exists on HTTPS or localhost, and
// players may open the page over plain http://<server address>.
(function (root) {
  'use strict';

  var N = BigInt('0x' +
    'AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310D' +
    'CD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF74' +
    '7359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D' +
    '5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB6' +
    '94B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73');
  var G = 2n;

  var K = new Uint32Array([
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01,
    0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
    0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
    0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08,
    0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
    0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2]);

  function sha256(bytes) {
    var len = bytes.length, total = ((len + 9 + 63) >> 6) << 6;
    var m = new Uint8Array(total);
    m.set(bytes);
    m[len] = 0x80;
    var bits = len * 8;
    for (var i = 0; i < 8; i++) m[total - 1 - i] = Math.floor(bits / Math.pow(2, 8 * i)) & 0xff;
    var h = new Uint32Array([0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]);
    var w = new Uint32Array(64);
    for (var off = 0; off < total; off += 64) {
      for (var t = 0; t < 16; t++)
        w[t] = (m[off + 4 * t] << 24) | (m[off + 4 * t + 1] << 16) | (m[off + 4 * t + 2] << 8) | m[off + 4 * t + 3];
      for (t = 16; t < 64; t++) {
        var x = w[t - 15], y = w[t - 2];
        var s0 = ((x >>> 7) | (x << 25)) ^ ((x >>> 18) | (x << 14)) ^ (x >>> 3);
        var s1 = ((y >>> 17) | (y << 15)) ^ ((y >>> 19) | (y << 13)) ^ (y >>> 10);
        w[t] = (w[t - 16] + s0 + w[t - 7] + s1) | 0;
      }
      var a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], k = h[7];
      for (t = 0; t < 64; t++) {
        var S1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
        var ch = (e & f) ^ (~e & g);
        var t1 = (k + S1 + ch + K[t] + w[t]) | 0;
        var S0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
        var maj = (a & b) ^ (a & c) ^ (b & c);
        var t2 = (S0 + maj) | 0;
        k = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
      }
      h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += k;
    }
    var out = new Uint8Array(32);
    for (i = 0; i < 8; i++) {
      out[4 * i] = h[i] >>> 24; out[4 * i + 1] = (h[i] >>> 16) & 0xff; out[4 * i + 2] = (h[i] >>> 8) & 0xff; out[4 * i + 3] = h[i] & 0xff;
    }
    return out;
  }

  function concat(a, b) { var r = new Uint8Array(a.length + b.length); r.set(a); r.set(b, a.length); return r; }

  // HMAC-SHA256 with the key's inner and outer pads prepared once (PBKDF2 calls it 10000 times with one key).
  function hmacFactory(key) {
    if (key.length > 64) key = sha256(key);
    var ipad = new Uint8Array(64), opad = new Uint8Array(64);
    for (var i = 0; i < 64; i++) { var k = i < key.length ? key[i] : 0; ipad[i] = k ^ 0x36; opad[i] = k ^ 0x5c; }
    return function (data) { return sha256(concat(opad, sha256(concat(ipad, data)))); };
  }

  function pbkdf2(password, salt, iterations, length) {
    var hmac = hmacFactory(password), out = new Uint8Array(length), blocks = Math.ceil(length / 32);
    for (var block = 1; block <= blocks; block++) {
      var u = hmac(concat(salt, new Uint8Array([block >>> 24, (block >>> 16) & 0xff, (block >>> 8) & 0xff, block & 0xff])));
      var t = u.slice();
      for (var n = 1; n < iterations; n++) { u = hmac(u); for (var j = 0; j < 32; j++) t[j] ^= u[j]; }
      out.set(t.subarray(0, Math.min(32, length - (block - 1) * 32)), (block - 1) * 32);
    }
    return out;
  }

  function toHex(bytes) {
    var s = '';
    for (var i = 0; i < bytes.length; i++) s += (bytes[i] < 16 ? '0' : '') + bytes[i].toString(16);
    return s.toUpperCase();
  }

  function fromHex(hex) {
    var out = new Uint8Array(hex.length / 2);
    for (var i = 0; i < out.length; i++) out[i] = parseInt(hex.substr(2 * i, 2), 16);
    return out;
  }

  function modPow(base, exp, mod) {
    var result = 1n;
    base %= mod;
    while (exp > 0n) {
      if (exp & 1n) result = (result * base) % mod;
      base = (base * base) % mod;
      exp >>= 1n;
    }
    return result;
  }

  // Salt (32 hex digits) and verifier (512 hex digits) for an account, as /srp/register takes them.
  function makeVerifier(password, saltHex) {
    var x = pbkdf2(new TextEncoder().encode(password), fromHex(saltHex), 10000, 32);
    var v = modPow(G, BigInt('0x' + toHex(x)), N).toString(16).toUpperCase();
    while (v.length < 512) v = '0' + v;
    return v;
  }

  function newSalt() {
    var bytes = new Uint8Array(16);
    (root.crypto || root.msCrypto).getRandomValues(bytes);
    return toHex(bytes);
  }

  root.LandmarkSrp = { makeVerifier: makeVerifier, newSalt: newSalt, sha256: sha256, pbkdf2: pbkdf2, toHex: toHex };
})(typeof window !== 'undefined' ? window : globalThis);
