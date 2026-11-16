// PiP Studio phone companion: the cryptography the pairing and every message use, in plain JavaScript.
// Browsers only offer their built-in crypto (crypto.subtle) on https pages, and this page comes from PiP Studio over
// plain http on your home network, so the few primitives needed are written out here, following their RFCs:
//   SHA-256 (FIPS 180-4), HMAC (RFC 2104), HKDF (RFC 5869), X25519 (RFC 7748), ChaCha20-Poly1305 (RFC 8439).
// The PC side (Companion.cs) uses .NET's own implementations, and the tests check both against the RFCs' test vectors.
// Randomness comes from crypto.getRandomValues, which browsers do allow on http pages.
"use strict";
(function (root) {
  const enc = new TextEncoder(), dec = new TextDecoder();

  // ---------------------------------------------------------------- bytes
  function concat(...parts) {
    let n = 0; for (const p of parts) n += p.length;
    const out = new Uint8Array(n); let o = 0;
    for (const p of parts) { out.set(p, o); o += p.length; }
    return out;
  }
  function random(n) { const b = new Uint8Array(n); crypto.getRandomValues(b); return b; }
  function b64(bytes) { let s = ""; for (const x of bytes) s += String.fromCharCode(x); return btoa(s); }
  function unb64(s) { const t = atob(s); const b = new Uint8Array(t.length); for (let i = 0; i < t.length; i++) b[i] = t.charCodeAt(i); return b; }
  function equal(a, b) { if (a.length !== b.length) return false; let d = 0; for (let i = 0; i < a.length; i++) d |= a[i] ^ b[i]; return d === 0; }

  // ---------------------------------------------------------------- SHA-256
  const K = new Uint32Array([
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
    0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
    0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
    0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2]);
  function sha256(data) {
    const len = data.length, bits = len * 8;
    const padded = new Uint8Array(((len + 9 + 63) >> 6) << 6);
    padded.set(data); padded[len] = 0x80;
    const dv = new DataView(padded.buffer);
    dv.setUint32(padded.length - 8, Math.floor(bits / 0x100000000)); dv.setUint32(padded.length - 4, bits >>> 0);
    const H = new Uint32Array([0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]);
    const W = new Uint32Array(64);
    for (let off = 0; off < padded.length; off += 64) {
      for (let i = 0; i < 16; i++) W[i] = dv.getUint32(off + i * 4);
      for (let i = 16; i < 64; i++) {
        const a = W[i - 15], b = W[i - 2];
        const s0 = ((a >>> 7) | (a << 25)) ^ ((a >>> 18) | (a << 14)) ^ (a >>> 3);
        const s1 = ((b >>> 17) | (b << 15)) ^ ((b >>> 19) | (b << 13)) ^ (b >>> 10);
        W[i] = (W[i - 16] + s0 + W[i - 7] + s1) >>> 0;
      }
      let [a, b, c, d, e, f, g, h] = H;
      for (let i = 0; i < 64; i++) {
        const S1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
        const t1 = (h + S1 + ((e & f) ^ (~e & g)) + K[i] + W[i]) >>> 0;
        const S0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
        const t2 = (S0 + ((a & b) ^ (a & c) ^ (b & c))) >>> 0;
        h = g; g = f; f = e; e = (d + t1) >>> 0; d = c; c = b; b = a; a = (t1 + t2) >>> 0;
      }
      H[0] += a; H[1] += b; H[2] += c; H[3] += d; H[4] += e; H[5] += f; H[6] += g; H[7] += h;
    }
    const out = new Uint8Array(32), ov = new DataView(out.buffer);
    for (let i = 0; i < 8; i++) ov.setUint32(i * 4, H[i]);
    return out;
  }

  function hmac(key, msg) {
    if (key.length > 64) key = sha256(key);
    const k = new Uint8Array(64); k.set(key);
    const ip = k.map((x) => x ^ 0x36), op = k.map((x) => x ^ 0x5c);
    return sha256(concat(op, sha256(concat(ip, msg))));
  }

  /** HKDF-SHA256 (RFC 5869), up to 255*32 bytes. */
  function hkdf(ikm, salt, info, len) {
    const prk = hmac(salt.length ? salt : new Uint8Array(32), ikm);
    const out = new Uint8Array(len);
    let t = new Uint8Array(0);
    for (let i = 1, o = 0; o < len; i++) {
      t = hmac(prk, concat(t, info, new Uint8Array([i])));
      out.set(t.subarray(0, Math.min(32, len - o)), o); o += 32;
    }
    return out;
  }

  // ---------------------------------------------------------------- X25519 (RFC 7748)
  const P = (1n << 255n) - 19n, A24 = 121665n;
  const md = (a) => { a %= P; return a < 0n ? a + P : a; };
  function powm(b, e) { let r = 1n; b = md(b); while (e > 0n) { if (e & 1n) r = r * b % P; b = b * b % P; e >>= 1n; } return r; }
  function leInt(b) { let x = 0n; for (let i = b.length - 1; i >= 0; i--) x = (x << 8n) | BigInt(b[i]); return x; }
  function leBytes(x, n) { const b = new Uint8Array(n); for (let i = 0; i < n; i++) { b[i] = Number(x & 255n); x >>= 8n; } return b; }

  function x25519(scalar, u) {
    const k = scalar.slice(); k[0] &= 248; k[31] &= 127; k[31] |= 64;
    const kk = leInt(k);
    const uu = u.slice(); uu[31] &= 127;
    const x1 = leInt(uu) % P;
    let x2 = 1n, z2 = 0n, x3 = x1, z3 = 1n, swap = 0n;
    for (let t = 254n; t >= 0n; t--) {
      const kt = (kk >> t) & 1n;
      swap ^= kt;
      if (swap) { [x2, x3] = [x3, x2]; [z2, z3] = [z3, z2]; }
      swap = kt;
      const A = md(x2 + z2), AA = A * A % P, B = md(x2 - z2), BB = B * B % P, E = md(AA - BB);
      const C = md(x3 + z3), D = md(x3 - z3), DA = D * A % P, CB = C * B % P;
      x3 = md((DA + CB) * (DA + CB));
      z3 = md(x1 * md((DA - CB) * (DA - CB)));
      x2 = AA * BB % P;
      z2 = md(E * md(AA + A24 * E));
    }
    if (swap) { [x2, x3] = [x3, x2]; [z2, z3] = [z3, z2]; }
    return leBytes(md(x2 * powm(z2, P - 2n)), 32);
  }
  const BASE = (() => { const b = new Uint8Array(32); b[0] = 9; return b; })();
  function keyPair() { const priv = random(32); return { priv, pub: x25519(priv, BASE) }; }

  // ---------------------------------------------------------------- ChaCha20-Poly1305 (RFC 8439)
  function chachaBlock(key, counter, nonce) {
    const s = new Uint32Array(16), kv = new DataView(key.buffer, key.byteOffset, 32), nv = new DataView(nonce.buffer, nonce.byteOffset, 12);
    s[0] = 0x61707865; s[1] = 0x3320646e; s[2] = 0x79622d32; s[3] = 0x6b206574;
    for (let i = 0; i < 8; i++) s[4 + i] = kv.getUint32(i * 4, true);
    s[12] = counter; s[13] = nv.getUint32(0, true); s[14] = nv.getUint32(4, true); s[15] = nv.getUint32(8, true);
    const x = s.slice();
    const R = (v, c) => (v << c) | (v >>> (32 - c));
    const Q = (a, b, c, d) => {
      x[a] += x[b]; x[d] = R(x[d] ^ x[a], 16); x[c] += x[d]; x[b] = R(x[b] ^ x[c], 12);
      x[a] += x[b]; x[d] = R(x[d] ^ x[a], 8); x[c] += x[d]; x[b] = R(x[b] ^ x[c], 7);
    };
    for (let i = 0; i < 10; i++) { Q(0, 4, 8, 12); Q(1, 5, 9, 13); Q(2, 6, 10, 14); Q(3, 7, 11, 15); Q(0, 5, 10, 15); Q(1, 6, 11, 12); Q(2, 7, 8, 13); Q(3, 4, 9, 14); }
    const out = new Uint8Array(64), ov = new DataView(out.buffer);
    for (let i = 0; i < 16; i++) ov.setUint32(i * 4, (x[i] + s[i]) >>> 0, true);
    return out;
  }
  function chachaXor(key, counter, nonce, data) {
    const out = new Uint8Array(data.length);
    for (let o = 0; o < data.length; o += 64, counter++) {
      const ks = chachaBlock(key, counter, nonce);
      for (let i = 0; i < 64 && o + i < data.length; i++) out[o + i] = data[o + i] ^ ks[i];
    }
    return out;
  }
  const P1305 = (1n << 130n) - 5n;
  function poly1305(otk, msg) {
    const r = leInt(otk.subarray(0, 16)) & 0x0ffffffc0ffffffc0ffffffc0fffffffn, s = leInt(otk.subarray(16, 32));
    let acc = 0n;
    for (let o = 0; o < msg.length; o += 16) {
      const blk = msg.subarray(o, Math.min(o + 16, msg.length));
      acc = ((acc + leInt(blk) + (1n << BigInt(8 * blk.length))) * r) % P1305;
    }
    return leBytes((acc + s) & ((1n << 128n) - 1n), 16);
  }
  function macData(aad, ct) {
    const pad = (n) => new Uint8Array((16 - (n % 16)) % 16);
    const lens = new Uint8Array(16), lv = new DataView(lens.buffer);
    lv.setUint32(0, aad.length, true); lv.setUint32(8, ct.length, true);
    return concat(aad, pad(aad.length), ct, pad(ct.length), lens);
  }
  /** Encrypts and authenticates: ciphertext followed by the 16-byte tag. */
  function seal(key, nonce, plain, aad) {
    const otk = chachaBlock(key, 0, nonce).subarray(0, 32);
    const ct = chachaXor(key, 1, nonce, plain);
    return concat(ct, poly1305(otk, macData(aad, ct)));
  }
  /** Checks the tag and decrypts, or returns null when anything was changed. */
  function open(key, nonce, sealed, aad) {
    if (sealed.length < 16) return null;
    const ct = sealed.subarray(0, sealed.length - 16), tag = sealed.subarray(sealed.length - 16);
    const otk = chachaBlock(key, 0, nonce).subarray(0, 32);
    if (!equal(poly1305(otk, macData(aad, ct)), tag)) return null;
    return chachaXor(key, 1, nonce, ct);
  }

  root.PipCrypto = { sha256, hmac, hkdf, x25519, keyPair, seal, open, random, concat, b64, unb64, equal, enc, dec, leBytes };
})(typeof window !== "undefined" ? window : globalThis);
