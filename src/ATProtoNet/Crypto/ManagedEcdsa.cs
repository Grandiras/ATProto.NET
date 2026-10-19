using System.Numerics;

namespace ATProtoNet.Crypto;

// ECDSA verification in managed code, for a curve the platform's ECDsa lacks (secp256k1 on macOS, CRY-16).
// It handles only public data, so it need not be constant-time; signing never comes here.
internal static class ManagedEcdsa
{
    // Whether r || s (both already in [1, n - 1]) signs the 32-byte hash for the public point x || y.
    public static bool Verify(Curve curve, ReadOnlySpan<byte> point, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature)
    {
        var n = curve.N;
        var r = Curve.Unsigned(signature[..32]);
        var w = BigInteger.ModPow(Curve.Unsigned(signature[32..]), n - 2, n);
        var u1 = Curve.Unsigned(hash) * w % n;
        var u2 = r * w % n;
        var g = new Jacobian(curve.Gx, curve.Gy, 1);
        var q = new Jacobian(Curve.Unsigned(point[..32]), Curve.Unsigned(point[32..]), 1);
        var sum = Add(curve, g, q);
        // Shamir's trick: u1·G + u2·Q in one double-and-add pass.
        var result = Jacobian.Infinity;
        for (var bit = 255; bit >= 0; bit--)
        {
            result = Double(curve, result);
            var (a, b) = (!(u1 >> bit).IsEven, !(u2 >> bit).IsEven);
            if (a || b) result = Add(curve, result, a && b ? sum : a ? g : q);
        }
        if (result.Z.IsZero) return false;
        var zInverse = BigInteger.ModPow(result.Z, curve.P - 2, curve.P);
        return curve.Mod(result.X * zInverse * zInverse) % n == r;
    }

    private static Jacobian Double(Curve c, Jacobian p)
    {
        if (p.Z.IsZero || p.Y.IsZero) return Jacobian.Infinity;
        var ySquared = c.Mod(p.Y * p.Y);
        var s = c.Mod(4 * p.X * ySquared);
        var zSquared = c.Mod(p.Z * p.Z);
        var m = c.Mod(3 * p.X * p.X + c.A * zSquared * zSquared);
        var x = c.Mod(m * m - 2 * s);
        return new(x, c.Mod(m * (s - x) - 8 * ySquared * ySquared), c.Mod(2 * p.Y * p.Z));
    }

    private static Jacobian Add(Curve c, Jacobian p, Jacobian q)
    {
        if (p.Z.IsZero) return q;
        if (q.Z.IsZero) return p;
        var (pz2, qz2) = (c.Mod(p.Z * p.Z), c.Mod(q.Z * q.Z));
        var (u1, u2) = (c.Mod(p.X * qz2), c.Mod(q.X * pz2));
        var (s1, s2) = (c.Mod(p.Y * qz2 * q.Z), c.Mod(q.Y * pz2 * p.Z));
        if (u1 == u2) return s1 == s2 ? Double(c, p) : Jacobian.Infinity;
        var (h, r) = (c.Mod(u2 - u1), c.Mod(s2 - s1));
        var (h2, h3) = (c.Mod(h * h), c.Mod(h * h * h));
        var x = c.Mod(r * r - h3 - 2 * u1 * h2);
        return new(x, c.Mod(r * (u1 * h2 - x) - s1 * h3), c.Mod(h * p.Z * q.Z));
    }

    private readonly record struct Jacobian(BigInteger X, BigInteger Y, BigInteger Z)
    {
        public static Jacobian Infinity => new(1, 1, 0);
    }
}
