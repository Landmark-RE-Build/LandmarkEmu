using System;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The client's QEF solver, ported line for line for <see cref="TerrainLod"/> (VoxelFarm::updateBlockLOD places a
    /// coarse vertex with it). It is a least-squares solve of A·x = b through a singular value decomposition, laid out
    /// like Ronen Tzur's dual-contouring qef.cpp: Householder bidiagonalisation, unpacking of U and V, implicit-shift QR
    /// sweeps with Givens rotations, sign / order fix of the singular values, then the back-substitution. A is rows × 3
    /// (one row per triangle: its unit normal), b one value per row.
    /// <para>0.695 addresses [P] (decompile re/decomp_all_695/part_0052.c, asm checked with capstone where the decompile
    /// hides the association of a sum or product):</para>
    /// <list type="bullet">
    /// <item>factorize FUN_142aa2900 (:26419), its Householder vector FUN_142aa2e70 (:26656);</item>
    /// <item>unpack FUN_142aa3140 (:26808);</item>
    /// <item>diagonalize FUN_142aa37f0 (:27078) → qrstep FUN_142aa3e50 (:27350), which hands off to FUN_142aa4aa0 (2 columns,
    /// :27805), FUN_142aa4480 (a zero on the diagonal, :27565) or FUN_142aa4800 (a zero at its end, :27698); the shift is
    /// FUN_142aa4a20 (:27780);</item>
    /// <item>singularize FUN_142aa53d0 (:28103); back-substitution FUN_142aa5700 (:28264).</item>
    /// </list>
    /// <para><b>Bit-exactness.</b> The client is MSVC x64 SSE2 code without FMA, and .NET's JIT does not fuse either, so the
    /// same operations in the same order give the same doubles. The order is the binary's, including two places where it
    /// is not what the C source would suggest [P asm]: the Givens rotations inlined into the QR code compute the tangent as
    /// (−1 / a) · b, not −b / a (0x142aa4026: divsd of −1.0, then mulsd); and the shift's product term is
    /// (u1·v1)·(u1·v1) (0x142aa4a45 / 0x142aa4a59), which Ghidra prints as u1·v1·u1·v1. Sums are accumulated one term at a
    /// time from the left (0x142aa2a42 unrolled dot product; 0x142aa58cd back-substitution). The standalone Givens
    /// FUN_142aa52a0 (−a / b, one division) has no caller and is not used.</para>
    /// <para>Matrices are flat row-major arrays with 3 columns (index row · 3 + column), as in the client's double[][3].</para>
    /// </summary>
    internal static class VoxelFarmQef
    {
        /// <summary>The largest system solved: updateBlockLOD solves only below 64 triangles (asm 0x142aae90d).</summary>
        public const int MaxRows = 64;

        /// <summary>
        /// Off-diagonal values below this × the neighbouring diagonal are dropped (DAT_144c41090 = 1e-5). So the client's
        /// decomposition is exact only to about 1e-5 of the matrix's size (TerrainLodQefTests checks it both ways).
        /// </summary>
        internal const double ChopEpsilon = 1e-5;

        /// <summary>Singular values below this are treated as 0 by the caller (DAT_144c41130 = 0.1).</summary>
        public const double SingularCut = 0.1;

        /// <summary>QR sweeps before diagonalize gives up (FUN_142aa37f0: local_730 = 1000).</summary>
        private const int MaxSweeps = 1000;

        /// <summary>Scratch arrays for one solve (one per worker thread; nothing is allocated per solve).</summary>
        public sealed class Workspace
        {
            /// <summary>U (rows × 3), the column copies diagonalize hands to qrstep, and V / its copy (3 × 3).</summary>
            internal readonly double[] U = new double[MaxRows * 3], U1 = new double[MaxRows * 3], V = new double[9], V1 = new double[9];
            /// <summary>Diagonal (tau_u, then the singular values) and super-diagonal (tau_v) of the bidiagonal matrix.</summary>
            internal readonly double[] D = new double[3], E = new double[2];
            /// <summary>The solution x of the last <see cref="Solve"/>.</summary>
            public readonly double[] X = new double[3];
        }

        /// <summary>
        /// One QEF solve as updateBlockLOD runs it (part_0052.c:33985-33998 and again :34044-34057): U = a copy of A,
        /// factorize, unpack, diagonalize, singularize, singular values below <see cref="SingularCut"/> set to 0 (d[2],
        /// d[1], d[0]), then x = V · diag(1/d) · Uᵀ · b into <see cref="Workspace.X"/>. A and b are not changed.
        /// </summary>
        public static void Solve(double[] a, double[] b, int rows, Workspace w, double chop = ChopEpsilon)
        {
            Decompose(a, rows, w, chop);
            if (w.D[2] < SingularCut)
                w.D[2] = 0.0;
            if (w.D[1] < SingularCut)
                w.D[1] = 0.0;
            if (w.D[0] < SingularCut)
                w.D[0] = 0.0;
            SolveSvd(w.U, w.V, w.D, b, w.X, rows);              // FUN_142aa5700
        }

        /// <summary>
        /// The decomposition A = U · diag(D) · Vᵀ into the workspace (qef.cpp's computeSVD; the client inlines it into
        /// updateBlockLOD): U = a copy of A, factorize, unpack, diagonalize, singularize. D is non-negative and sorted
        /// decreasing. A is not changed. <paramref name="chop"/> is the client's <see cref="ChopEpsilon"/> except in tests.
        /// </summary>
        internal static void Decompose(double[] a, int rows, Workspace w, double chop = ChopEpsilon)
        {
            Array.Copy(a, w.U, rows * 3);                       // FUN_1437ecb60 = memcpy
            Factorize(w.U, w.D, w.E, rows);                     // FUN_142aa2900
            Unpack(w.U, w.V, w.D, w.E, rows);                   // FUN_142aa3140
            Diagonalize(w.U, w.V, w.D, w.E, rows, w, chop);     // FUN_142aa37f0
            Singularize(w.U, w.V, w.D, rows);                   // FUN_142aa53d0
        }

        /// <summary>
        /// FUN_142aa2900: bidiagonal factorization of the rows × 3 matrix in place. Column y gets a Householder vector
        /// below the diagonal (tau_u[y]) applied from the left to the columns after it; row 0 gets one right of the
        /// super-diagonal (tau_v[0], 2 elements) applied from the right to the rows below. tau_v[1] is 0 (a 1-element row).
        /// </summary>
        private static void Factorize(double[] mat, double[] tauU, double[] tauV, int rows)
        {
            for (int y = 0; y < 3; y++)
            {
                int num = rows - y;
                // Column y from the diagonal down: mat[(y + i) * 3 + y], i = 0 .. num - 1.
                tauU[y] = num < 2 ? 0.0 : Householder(mat, y * 3 + y, 3, num);
                if (y + 1 < 3)
                {
                    if (tauU[y] != 0.0)
                        for (int x = y + 1; x < 3; x++)
                        {
                            double f = mat[y * 3 + x];
                            for (int i = 1; i < num; i++)
                                f = f + mat[(y + i) * 3 + y] * mat[(y + i) * 3 + x];
                            f = f * tauU[y];
                            mat[y * 3 + x] = mat[y * 3 + x] - f;
                            for (int i = 1; i < num; i++)
                                mat[(y + i) * 3 + x] = mat[(y + i) * 3 + x] - f * mat[(y + i) * 3 + y];
                        }
                    // Row y right of the diagonal: 2 elements for y = 0 (mat[0][1], mat[0][2]), 1 for y = 1 (no vector).
                    tauV[y] = y == 0 ? Householder(mat, 1, 1, 2) : 0.0;
                    if (tauV[y] != 0.0)
                        for (int x = y + 1; x < rows; x++)
                        {
                            // y is 0 here: the row vector is (1, mat[0][2]).
                            double f = mat[x * 3 + 1] + mat[x * 3 + 2] * mat[2];
                            f = f * tauV[y];
                            mat[x * 3 + 1] = mat[x * 3 + 1] - f;
                            mat[x * 3 + 2] = mat[x * 3 + 2] - f * mat[2];
                        }
                }
            }
        }

        /// <summary>
        /// FUN_142aa2e70: the Householder vector of the n values at <paramref name="start"/>, <paramref name="step"/>
        /// apart (the client passes an array of pointers). The first value becomes beta, the others are scaled to the
        /// vector's tail; returns tau (0 when the tail is already zero). The tail's norm is a scaled sum of squares
        /// (LAPACK dnrm2 style); for n = 2 it is |p[1]|.
        /// </summary>
        private static double Householder(double[] m, int start, int step, int n)
        {
            double xnorm;
            if (n == 2)
                xnorm = Math.Abs(m[start + step]);
            else
            {
                double scl = 0.0, ssq = 1.0;
                for (int i = 1; i < n; i++)
                {
                    double x = Math.Abs(m[start + i * step]);
                    if (x != 0.0)
                    {
                        if (scl >= x)
                        {
                            double t = x / scl;
                            ssq = ssq + t * t;
                        }
                        else
                        {
                            double t = scl / x;
                            ssq = t * ssq * t + 1.0;            // ((t * ssq) * t) + 1 [P asm 0x142aa2f07]
                            scl = x;
                        }
                    }
                }
                xnorm = Math.Sqrt(ssq) * scl;
            }
            if (xnorm == 0.0)
                return 0.0;
            double alpha = m[start];
            double beta = Math.Sqrt(alpha * alpha + xnorm * xnorm);
            if (alpha >= 0.0)
                beta = -beta;
            m[start] = beta;
            double scal = 1.0 / (alpha - beta);
            for (int i = 1; i < n; i++)
                m[start + i * step] = scal * m[start + i * step];
            return (beta - alpha) / beta;
        }

        /// <summary>
        /// FUN_142aa3140: builds V (3 × 3) from the row Householder vectors and U (rows × 3, in place) from the column
        /// ones, moving the bidiagonal into tau_u (diagonal) and tau_v (super-diagonal).
        /// </summary>
        private static void Unpack(double[] u, double[] v, double[] tauU, double[] tauV, int rows)
        {
            v[0] = v[4] = v[8] = 1.0;
            v[1] = v[2] = v[3] = v[5] = v[6] = v[7] = 0.0;
            for (int i = 1; i >= 0; i--)
                if (tauV[i] != 0.0)
                    for (int x = i + 1; x < 3; x++)
                    {
                        double f = v[(i + 1) * 3 + x];
                        for (int y = i + 2; y < 3; y++)
                            f = f + u[i * 3 + y] * v[y * 3 + x];
                        f = f * tauV[i];
                        v[(i + 1) * 3 + x] = v[(i + 1) * 3 + x] - f;
                        for (int y = i + 2; y < 3; y++)
                            v[y * 3 + x] = v[y * 3 + x] - f * u[i * 3 + y];
                    }

            tauV[0] = u[0 * 3 + 1];
            tauV[1] = u[1 * 3 + 2];

            for (int i = 2; i >= 0; i--)
            {
                double tau = tauU[i];
                tauU[i] = u[i * 3 + i];
                if (tau == 0.0)
                {
                    u[i * 3 + i] = 1.0;
                    for (int x = i + 1; x < 3; x++)
                        u[i * 3 + x] = 0.0;
                    for (int y = i + 1; y < rows; y++)
                        u[y * 3 + i] = 0.0;
                }
                else
                {
                    for (int x = i + 1; x < 3; x++)
                    {
                        double f = 0.0;
                        for (int y = i + 1; y < rows; y++)
                            f = f + u[y * 3 + x] * u[y * 3 + i];
                        f = f * tau;
                        u[i * 3 + x] = -f;
                        for (int y = i + 1; y < rows; y++)
                            u[y * 3 + x] = u[y * 3 + x] - f * u[y * 3 + i];
                    }
                    for (int y = i + 1; y < rows; y++)
                        u[y * 3 + i] = -(tau * u[y * 3 + i]);
                    u[i * 3 + i] = 1.0 - tau;
                }
            }
        }

        /// <summary>
        /// FUN_142aa37f0: QR sweeps on the unreduced block [a, b] of the bidiagonal until the super-diagonal is gone (at
        /// most <see cref="MaxSweeps"/> passes, counting the passes that only shrink b). The block's columns of U and V are
        /// copied out for qrstep and back afterwards, as the client does.
        /// </summary>
        private static void Diagonalize(double[] u, double[] v, double[] tauU, double[] tauV, int rows, Workspace w, double chop)
        {
            Chop(tauU, 0, tauV, 0, 3, chop);
            int b = 2;
            for (int guard = MaxSweeps; ;)
            {
                if (guard < 1)
                    return;
                guard--;
                if (tauV[b - 1] == 0.0)
                    b--;
                else
                {
                    int a = b - 1;
                    while (a > 0 && tauV[a - 1] != 0.0)
                        a--;
                    int n = b - a + 1;
                    double[] u1 = w.U1, v1 = w.V1;
                    for (int j = a; j <= b; j++)
                    {
                        for (int i = 0; i < rows; i++)
                            u1[i * 3 + j - a] = u[i * 3 + j];
                        for (int i = 0; i < 3; i++)
                            v1[i * 3 + j - a] = v[i * 3 + j];
                    }
                    QrStep(u1, v1, tauU, a, tauV, a, rows, n);
                    for (int j = a; j <= b; j++)
                    {
                        for (int i = 0; i < rows; i++)
                            u[i * 3 + j] = u1[i * 3 + j - a];
                        for (int i = 0; i < 3; i++)
                            v[i * 3 + j] = v1[i * 3 + j - a];
                    }
                    Chop(tauU, a, tauV, a, n, chop);
                }
                if (b < 1)
                    return;
            }
        }

        /// <summary>Drops super-diagonal values below <see cref="ChopEpsilon"/> × (|their two diagonal neighbours|).</summary>
        private static void Chop(double[] d, int da, double[] e, int ea, int n, double chop)
        {
            for (int i = 0; i < n - 1; i++)
                if (Math.Abs(e[ea + i]) < (Math.Abs(d[da + i]) + Math.Abs(d[da + i + 1])) * chop)
                    e[ea + i] = 0.0;
        }

        /// <summary>
        /// The Givens rotation the QR code inlines (four copies in FUN_142aa3e50, FUN_142aa4480, FUN_142aa4800,
        /// FUN_142aa4aa0): (c, s) with c·a − s·b = r, s·a + c·b = 0. The tangent is (−1 / a) · b [P asm 0x142aa4026].
        /// </summary>
        private static void Givens(double a, double b, out double c, out double s)
        {
            if (b == 0.0)
            {
                c = 1.0;
                s = 0.0;
            }
            else if (Math.Abs(b) <= Math.Abs(a))
            {
                double t = (-1.0 / a) * b;
                c = 1.0 / Math.Sqrt(t * t + 1.0);
                s = c * t;
            }
            else
            {
                double t = (-1.0 / b) * a;
                s = 1.0 / Math.Sqrt(t * t + 1.0);
                c = s * t;
            }
        }

        /// <summary>
        /// FUN_142aa3e50: one implicit-shift QR sweep over an n-column block (diagonal tau_u[du..], super-diagonal
        /// tau_v[dv..]). Two columns go to <see cref="QrStep2"/>; a zero on the diagonal to <see cref="QrStepMiddle"/>, a
        /// zero at its end to <see cref="QrStepEnd"/>. U and V here are the block's column copies (column 0 = column a).
        /// </summary>
        private static void QrStep(double[] u, double[] v, double[] tauU, int du, double[] tauV, int dv, int rows, int cols)
        {
            if (cols == 2)
            {
                QrStep2(u, v, tauU, du, tauV, dv, rows);
                return;
            }
            for (int i = 0; i < cols - 1; i++)
                if (tauU[du + i] == 0.0)
                {
                    QrStepMiddle(u, tauU, du, tauV, dv, rows, cols, i);
                    return;
                }
            if (tauU[du + cols - 1] == 0.0)
            {
                QrStepEnd(v, tauU, du, tauV, dv);
                return;
            }

            double mu = Eigenvalue(tauU, du, tauV, dv);
            double y = tauU[du] * tauU[du] - mu;
            double z = tauU[du] * tauV[dv];
            double bk = 0.0, zk;
            double ap = tauU[du], bp = tauV[dv], aq = tauU[du + 1];
            for (int k = 0; k < cols - 1; k++)
            {
                // Rotation on V (from the right).
                Givens(y, z, out double c, out double s);
                for (int i = 0; i < 3; i++)
                {
                    double vip = v[i * 3 + k], viq = v[i * 3 + k + 1];
                    v[i * 3 + k] = vip * c - viq * s;
                    v[i * 3 + k + 1] = viq * c + vip * s;
                }
                double bk1 = c * bk - s * z;
                double ap1 = c * ap - s * bp;
                double bp1 = c * bp + s * ap;
                double zp1 = -(s * aq);
                double aq1 = c * aq;
                if (k > 0)
                    tauV[dv + k - 1] = bk1;
                double ak = ap1;
                bk = bp1;
                zk = zp1;
                ap = aq1;
                bp = k < cols - 2 ? tauV[dv + k + 1] : 0.0;
                y = ak;
                z = zk;

                // Rotation on U (from the left).
                Givens(y, z, out c, out s);
                for (int i = 0; i < rows; i++)
                {
                    double uip = u[i * 3 + k], uiq = u[i * 3 + k + 1];
                    u[i * 3 + k] = uip * c - uiq * s;
                    u[i * 3 + k + 1] = uiq * c + uip * s;
                }
                double ak1 = c * ak - zk * s;
                bk1 = bk * c - ap * s;
                double zk1 = -(s * bp);
                ap1 = ap * c + bk * s;
                bp1 = bp * c;
                tauU[du + k] = ak1;
                bk = bk1;
                zk = zk1;
                ap = ap1;
                bp = bp1;
                aq = k < cols - 2 ? tauU[du + k + 2] : 0.0;
                y = bk;
                z = zk;
            }
            tauV[dv + cols - 2] = bk;
            tauU[du + cols - 1] = ap;
        }

        /// <summary>
        /// FUN_142aa4480: chases out the super-diagonal value right of a zero diagonal value at column
        /// <paramref name="col"/> with rotations from the left (U only).
        /// </summary>
        private static void QrStepMiddle(double[] u, double[] tauU, int du, double[] tauV, int dv, int rows, int cols, int col)
        {
            double x = tauU[du + col + 1];
            double y = tauV[dv + col];
            for (int j = col; j < cols - 1; j++)
            {
                Givens(x, -y, out double c, out double s);
                for (int i = 0; i < rows; i++)
                {
                    double uip = u[i * 3 + col], uiq = u[i * 3 + j + 1];
                    u[i * 3 + col] = uip * c - uiq * s;
                    u[i * 3 + j + 1] = uiq * c + uip * s;
                }
                tauU[du + j + 1] = c * x + s * y;
                if (j == col)
                    tauV[dv + j] = c * y - s * x;
                if (j < cols - 2)
                {
                    double z = tauV[dv + j + 1];
                    y = -(z * s);
                    tauV[dv + j + 1] = z * c;
                    x = tauU[du + j + 2];
                }
            }
        }

        /// <summary>
        /// FUN_142aa4800 (3 columns, the only case it is called for): a zero at the end of the diagonal; rotations from the
        /// right (V only) chase the last super-diagonal value up.
        /// </summary>
        private static void QrStepEnd(double[] v, double[] tauU, int du, double[] tauV, int dv)
        {
            double x = tauU[du + 1];
            double z = tauV[dv + 1];
            for (int k = 1; k >= 0; k--)
            {
                Givens(x, z, out double c, out double s);
                for (int i = 0; i < 3; i++)
                {
                    double vip = v[i * 3 + k], viq = v[i * 3 + 2];
                    v[i * 3 + k] = vip * c - viq * s;
                    v[i * 3 + 2] = viq * c + vip * s;
                }
                tauU[du + k] = c * x - s * z;
                if (k == 1)
                {
                    tauV[dv + k] = c * z + s * x;
                    double w = tauV[dv + k - 1];
                    z = w * s;
                    tauV[dv + k - 1] = w * c;
                    x = tauU[du + k - 1];
                }
            }
        }

        /// <summary>FUN_142aa4a20: the Wilkinson shift from the trailing 2 × 2 of BᵀB (3 columns).</summary>
        private static double Eigenvalue(double[] tauU, int du, double[] tauV, int dv)
        {
            double ta = tauV[dv + 1] * tauV[dv + 1] + tauU[du + 2] * tauU[du + 2];
            double tab = tauU[du + 1] * tauV[dv + 1];
            tab = tab * tab;                                    // (u1·v1)² [P asm 0x142aa4a45, 0x142aa4a59]
            double dt = ((tauV[dv] * tauV[dv] + tauU[du + 1] * tauU[du + 1]) - ta) * 0.5;
            double sq = Math.Sqrt(dt * dt + tab);
            return dt >= 0.0 ? ta - tab / (sq + dt) : tab / (sq - dt) + ta;
        }

        /// <summary>
        /// FUN_142aa4aa0: the 2 × 2 block [a0 b0; 0 a1]. A zero a0 is rotated away from the left and V's columns swap;
        /// a zero a1 from the right; otherwise one Jacobi rotation of BᵀB from the right (V), a column swap that keeps the
        /// larger value first, and a rotation from the left (U) that clears b0.
        /// </summary>
        private static void QrStep2(double[] u, double[] v, double[] tauU, int du, double[] tauV, int dv, int rows)
        {
            if (tauU[du] == 0.0)
            {
                double a1 = tauU[du + 1], b0 = tauV[dv];
                Givens(b0, a1, out double c, out double s);
                tauU[du] = b0 * c - a1 * s;
                tauV[dv] = a1 * c + s * b0;
                tauU[du + 1] = 0.0;
                for (int i = 0; i < rows; i++)
                {
                    double u0 = u[i * 3], u1 = u[i * 3 + 1];
                    u[i * 3 + 1] = u1 * c + u0 * s;
                    u[i * 3] = u0 * c - u1 * s;
                }
                for (int i = 0; i < 3; i++)
                    (v[i * 3], v[i * 3 + 1]) = (v[i * 3 + 1], v[i * 3]);
                return;
            }
            if (tauU[du + 1] == 0.0)
            {
                double a0 = tauU[du], b0 = tauV[dv];
                Givens(a0, b0, out double c, out double s);
                tauU[du] = a0 * c - b0 * s;
                tauV[dv] = 0.0;
                for (int i = 0; i < 3; i++)
                {
                    double v0 = v[i * 3], v1 = v[i * 3 + 1];
                    v[i * 3 + 1] = v1 * c + v0 * s;
                    v[i * 3] = v0 * c - v1 * s;
                }
                return;
            }
            {
                double a0 = tauU[du], a1 = tauU[du + 1], b0 = tauV[dv];
                double c, s;
                double denom = a0 * b0 * 2.0;                   // a power-of-two factor: exact in either order
                if (denom == 0.0)
                {
                    s = 0.0;
                    c = 1.0;
                }
                else
                {
                    double t = ((a1 - a0) * (a0 + a1) + b0 * b0) / denom;
                    double r = Math.Sqrt(t * t + 1.0);
                    double sign;
                    if (t < 0.0)
                    {
                        r = r - t;
                        sign = -1.0;
                    }
                    else
                    {
                        r = r + t;
                        sign = 1.0;
                    }
                    t = sign / r;
                    c = 1.0 / Math.Sqrt(t * t + 1.0);
                    s = t * c;
                }
                double alpha = a0 * c - b0 * s;
                double beta = b0 * c + a0 * s;
                double gamma = a1 * c;
                double delta = -(a1 * s);
                for (int i = 0; i < 3; i++)
                {
                    double v0 = v[i * 3], v1 = v[i * 3 + 1];
                    v[i * 3] = v0 * c - v1 * s;
                    v[i * 3 + 1] = v1 * c + v0 * s;
                }
                double p, q, r0, r1;                            // [p r0; q r1] after the optional swap
                if (delta * delta + alpha * alpha < gamma * gamma + beta * beta)
                {
                    for (int i = 0; i < 3; i++)
                        (v[i * 3], v[i * 3 + 1]) = (v[i * 3 + 1], v[i * 3]);
                    p = beta;
                    q = gamma;
                    r0 = alpha;
                    r1 = delta;
                }
                else
                {
                    p = alpha;
                    q = delta;
                    r0 = beta;
                    r1 = gamma;
                }
                Givens(p, q, out double c2, out double s2);
                tauU[du] = p * c2 - q * s2;
                tauV[dv] = r0 * c2 - r1 * s2;
                tauU[du + 1] = r1 * c2 + r0 * s2;
                for (int i = 0; i < rows; i++)
                {
                    double u0 = u[i * 3], u1 = u[i * 3 + 1];
                    u[i * 3 + 1] = u1 * c2 + u0 * s2;
                    u[i * 3] = u0 * c2 - u1 * s2;
                }
            }
        }

        /// <summary>
        /// FUN_142aa53d0: makes the singular values non-negative (negating V's column) and sorts them in decreasing
        /// order, swapping U's and V's columns with them.
        /// </summary>
        private static void Singularize(double[] u, double[] v, double[] d, int rows)
        {
            for (int j = 0; j < 3; j++)
                if (d[j] < 0.0)
                {
                    for (int i = 0; i < 3; i++)
                        v[i * 3 + j] = -v[i * 3 + j];
                    d[j] = -d[j];
                }
            for (int i = 0; i < 3; i++)
            {
                double dmax = d[i];
                int imax = i;
                for (int j = i + 1; j < 3; j++)
                    if (dmax < d[j])
                    {
                        imax = j;
                        dmax = d[j];
                    }
                if (imax == i)
                    continue;
                (d[i], d[imax]) = (d[imax], d[i]);
                for (int y = 0; y < rows; y++)
                    (u[y * 3 + i], u[y * 3 + imax]) = (u[y * 3 + imax], u[y * 3 + i]);
                for (int y = 0; y < 3; y++)
                    (v[y * 3 + i], v[y * 3 + imax]) = (v[y * 3 + imax], v[y * 3 + i]);
            }
        }

        /// <summary>
        /// FUN_142aa5700: x = V · w, w = diag(1/d) · Uᵀ · b, skipping rows whose b is 0 and singular values that are 0
        /// (the client starts w from a static zero vector, DAT_145eb4b68 in .bss).
        /// </summary>
        private static void SolveSvd(double[] u, double[] v, double[] d, double[] b, double[] x, int rows)
        {
            double w0 = 0.0, w1 = 0.0, w2 = 0.0;
            for (int i = 0; i < rows; i++)
            {
                double bi = b[i];
                if (bi != 0.0)
                {
                    w0 = w0 + bi * u[i * 3];
                    w1 = w1 + bi * u[i * 3 + 1];
                    w2 = w2 + bi * u[i * 3 + 2];
                }
            }
            if (d[0] != 0.0)
                w0 = w0 / d[0];
            if (d[1] != 0.0)
                w1 = w1 / d[1];
            if (d[2] != 0.0)
                w2 = w2 / d[2];
            x[0] = w0 * v[0] + w1 * v[1] + w2 * v[2];
            x[1] = w0 * v[3] + w1 * v[4] + w2 * v[5];
            x[2] = w0 * v[6] + w1 * v[7] + w2 * v[8];
        }
    }
}
