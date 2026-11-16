using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace PipStudio
{
    /// <summary>
    /// The cryptography behind the phone companion's pairing and messages (companion\crypto.js is the phone's copy).
    ///
    /// Pairing (like Moonlight and Sunshine: the phone shows a PIN, you type it into PiP Studio):
    ///   1. phone → PC   commit = SHA-256(phone key ‖ phone nonce)          (the phone is now locked to its key)
    ///   2. PC → phone   PC key, PC nonce                                    (fresh X25519 key for this pairing)
    ///   3. phone → PC   phone key, phone nonce                             (must match the commitment)
    ///   Both compute the X25519 shared secret and a 6-digit PIN from it and the whole exchange. The phone shows its PIN;
    ///   you type it into PiP Studio, which accepts it only if it matches its own. Someone in the middle would have run two
    ///   separate exchanges and gets two different PINs, and because the phone committed to its key first, they can't try
    ///   key after key until the PINs agree: one try, a one-in-a-million chance.
    ///   Then each side derives the same 32-byte device key (HKDF-SHA256), and two directional keys from it.
    ///
    /// Messages: ChaCha20-Poly1305 with a random 12-byte nonce, the device id (and for answers the question's nonce) as
    /// associated data, a timestamp and a remembered-nonce list against replays.
    /// </summary>
    static class CompanionCrypto
    {
        public static readonly byte[] PairLabel = Encoding.ASCII.GetBytes("pipstudio-pair-v1");

        // ------------------------------------------------------------ X25519 (RFC 7748)
        static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        static readonly BigInteger A24 = 121665;

        static BigInteger Le(byte[] b) { return new BigInteger(b, isUnsigned: true, isBigEndian: false); }

        static byte[] LeBytes(BigInteger x)
        {
            var raw = x.ToByteArray(isUnsigned: true, isBigEndian: false);
            var b = new byte[32];
            Array.Copy(raw, b, Math.Min(32, raw.Length));
            return b;
        }

        static BigInteger M(BigInteger a) { a %= P; return a.Sign < 0 ? a + P : a; }

        public static byte[] X25519(byte[] scalar, byte[] u)
        {
            var k = (byte[])scalar.Clone();
            k[0] &= 248; k[31] &= 127; k[31] |= 64;
            var kk = Le(k);
            var uu = (byte[])u.Clone();
            uu[31] &= 127;
            var x1 = Le(uu) % P;
            BigInteger x2 = 1, z2 = 0, x3 = x1, z3 = 1;
            int swap = 0;
            for (int t = 254; t >= 0; t--)
            {
                int kt = (int)((kk >> t) & 1);
                swap ^= kt;
                if (swap == 1) { var a = x2; x2 = x3; x3 = a; a = z2; z2 = z3; z3 = a; }
                swap = kt;
                var A = M(x2 + z2); var AA = A * A % P; var B = M(x2 - z2); var BB = B * B % P; var E = M(AA - BB);
                var C = M(x3 + z3); var D = M(x3 - z3); var DA = D * A % P; var CB = C * B % P;
                x3 = M((DA + CB) * (DA + CB));
                z3 = M(x1 * M((DA - CB) * (DA - CB)));
                x2 = AA * BB % P;
                z2 = M(E * M(AA + A24 * E));
            }
            if (swap == 1) { var a = x2; x2 = x3; x3 = a; a = z2; z2 = z3; z3 = a; }
            return LeBytes(M(x2 * BigInteger.ModPow(z2, P - 2, P)));
        }

        static readonly byte[] Base = MakeBase();
        static byte[] MakeBase() { var b = new byte[32]; b[0] = 9; return b; }

        public static void KeyPair(out byte[] priv, out byte[] pub)
        {
            priv = RandomNumberGenerator.GetBytes(32);
            pub = X25519(priv, Base);
        }

        // ------------------------------------------------------------ the pairing's results
        static byte[] Cat(params byte[][] parts)
        {
            int n = 0; foreach (var p in parts) n += p.Length;
            var o = new byte[n]; int at = 0;
            foreach (var p in parts) { Buffer.BlockCopy(p, 0, o, at, p.Length); at += p.Length; }
            return o;
        }

        public static byte[] Commit(byte[] phonePub, byte[] phoneNonce) { return SHA256.HashData(Cat(phonePub, phoneNonce)); }

        /// <summary>The shared secret, or null for a key that gives no secret (all-zero: a broken or hostile key).</summary>
        public static byte[] Shared(byte[] myPriv, byte[] theirPub)
        {
            if (theirPub == null || theirPub.Length != 32) return null;
            var s = X25519(myPriv, theirPub);
            int any = 0; foreach (var x in s) any |= x;
            return any == 0 ? null : s;
        }

        static byte[] Transcript(byte[] phonePub, byte[] pcPub, byte[] phoneNonce, byte[] pcNonce)
        {
            return SHA256.HashData(Cat(PairLabel, phonePub, pcPub, phoneNonce, pcNonce));
        }

        /// <summary>The 6-digit PIN both sides show (or check) for this exchange.</summary>
        public static string Pin(byte[] shared, byte[] phonePub, byte[] pcPub, byte[] phoneNonce, byte[] pcNonce)
        {
            var b = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 4, Transcript(phonePub, pcPub, phoneNonce, pcNonce), Encoding.ASCII.GetBytes("pin"));
            uint v = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            return (v % 1000000).ToString("D6");
        }

        /// <summary>The phone's long-term key: the same 32 bytes on both sides after a matching PIN.</summary>
        public static byte[] DeviceKey(byte[] shared, byte[] phonePub, byte[] pcPub, byte[] phoneNonce, byte[] pcNonce)
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, Transcript(phonePub, pcPub, phoneNonce, pcNonce), Encoding.ASCII.GetBytes("device-key"));
        }

        /// <summary>The key for one direction: "c2s" (phone to PC) or "s2c" (PC to phone).</summary>
        public static byte[] Direction(byte[] deviceKey, string dir)
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, deviceKey, 32, new byte[0], Encoding.ASCII.GetBytes(dir));
        }

        // ------------------------------------------------------------ messages
        public static byte[] Seal(byte[] key, byte[] nonce, byte[] plain, byte[] aad)
        {
            var ct = new byte[plain.Length];
            var tag = new byte[16];
            using (var c = new ChaCha20Poly1305(key)) c.Encrypt(nonce, plain, ct, tag, aad);
            return Cat(ct, tag);
        }

        /// <summary>Decrypts, or null when the message was changed or isn't for this key.</summary>
        public static byte[] Open(byte[] key, byte[] nonce, byte[] sealedData, byte[] aad)
        {
            if (nonce == null || nonce.Length != 12 || sealedData == null || sealedData.Length < 16) return null;
            var ct = new byte[sealedData.Length - 16];
            var tag = new byte[16];
            Buffer.BlockCopy(sealedData, 0, ct, 0, ct.Length);
            Buffer.BlockCopy(sealedData, ct.Length, tag, 0, 16);
            var plain = new byte[ct.Length];
            try { using (var c = new ChaCha20Poly1305(key)) c.Decrypt(nonce, ct, tag, plain, aad); }
            catch (CryptographicException) { return null; }
            return plain;
        }

        // ------------------------------------------------------------ keeping device keys on disk
        static readonly byte[] Entropy = Encoding.ASCII.GetBytes("PiP Studio phone companion");

        /// <summary>Locks a device key to this Windows account (DPAPI), so a copied settings file is useless elsewhere.</summary>
        public static string Protect(byte[] key)
        {
            return Convert.ToBase64String(ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));
        }

        public static byte[] Unprotect(string s)
        {
            try { return ProtectedData.Unprotect(Convert.FromBase64String(s), Entropy, DataProtectionScope.CurrentUser); }
            catch { return null; }
        }
    }
}
