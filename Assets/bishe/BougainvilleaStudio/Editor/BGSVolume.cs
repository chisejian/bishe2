#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BougainvilleaStudio
{
    // Paths are skeletons. Separate bent alpha patches form the foliage.
    // No enclosing tube, solid foliage lumps, or backing wall is generated.
    internal static class BGSVolume
    {
        static readonly Vector2[] outline = {
            new Vector2(.12f,.08f), new Vector2(.5f,0),
            new Vector2(.90f,.10f), new Vector2(1,.5f),
            new Vector2(.88f,.92f), new Vector2(.5f,1),
            new Vector2(.10f,.90f), new Vector2(0,.5f)
        };
        struct Frame { public Vector3 position, tangent, right, normal; }
        sealed class Path
        {
            readonly List<Vector3> points;
            readonly Vector3[] rights;
            public readonly float length;
            public Path(BGSAuthoring t, BGSBranch branch, IBGSSurfaceQuery query)
            {
                float rawLength = BGSCore.Length(t, BGSCore.Smooth(branch.points));
                int steps = Mathf.Clamp(Mathf.CeilToInt(rawLength / .12f), 8, 4096);
                points = BGSCore.SnappedCurve(t, branch, query, steps);
                rights = new Vector3[points.Count];
                length = BGSCore.Length(t, points);
                Vector3 previous = Vector3.right;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 right = Vector3.ProjectOnPlane(previous, Tangent(i)).normalized;
                    if (right.sqrMagnitude < .01f) { Vector3 unused; Basis(Tangent(i), out right, out unused); }
                    rights[i] = right; previous = right;
                }
            }
            Vector3 Tangent(int i)
            {
                Vector3 d = points[Mathf.Min(points.Count - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
                return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.down;
            }
            public Frame At(float u)
            {
                float where = Mathf.Clamp01(u) * (points.Count - 1);
                int i = Mathf.Min(points.Count - 2, Mathf.FloorToInt(where)); float v = where - i;
                Vector3 tangent = Vector3.Lerp(Tangent(i), Tangent(i + 1), v).normalized;
                if (tangent.sqrMagnitude < .01f) tangent = Tangent(i);
                Vector3 right = Vector3.ProjectOnPlane(Vector3.Lerp(rights[i], rights[i + 1], v), tangent).normalized;
                if (right.sqrMagnitude < .01f) { Vector3 unused; Basis(tangent, out right, out unused); }
                return new Frame { position = Vector3.Lerp(points[i], points[i + 1], v), tangent = tangent,
                    right = right, normal = Vector3.Cross(tangent, right).normalized };
            }
        }
        static float R(System.Random r, float a, float b) { return BGSCore.Range(r, a, b); }
        static int Seed(int seed, int n) { unchecked { return seed * 486187739 + n * 16777619; } }
        static Color Tint(Color a, Color b) { return new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1); }
        static void Basis(Vector3 normal, out Vector3 right, out Vector3 up)
        {
            right = Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
            if (right.sqrMagnitude < .01f) right = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            up = Vector3.Cross(normal, right).normalized;
        }
        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u)
        { float v = 1 - u; return a * (v * v) + b * (2 * v * u) + c * (u * u); }
        static float Activity(float distance, int seed)
        {
            float phase = (seed & 65535) * .001f;
            return Mathf.Clamp01(.52f + .31f * Mathf.Sin(distance * 1.83f + phase)
                + .22f * Mathf.Sin(distance * 4.17f + phase * 1.83f));
        }
        static float Envelope(float u, float distance, int seed)
        {
            float taper = Mathf.Lerp(1, .24f, Mathf.Pow(Mathf.Clamp01((u - .55f) / .45f), 1.25f));
            return taper * Mathf.Lerp(.50f, 1, Activity(distance, seed));
        }
        static float PatchSize(BGSAuthoring t)
        {
            float envelope = t.mode == BGSMode.FlowerWall
                ? Mathf.Min(t.wallWidth, t.wallHeight, Mathf.Max(.08f, t.volumeDepth * 1.8f))
                : Mathf.Min(t.branchWidth, t.volumeDepth);
            return Mathf.Max(.004f, Mathf.Min(t.textureMetres * .80f, envelope * .90f));
        }
        static Vector2 AtlasUV(Vector2 p, int tile)
        {
            // UV origin is bottom left. Sample only the lower transparent row.
            return new Vector2((tile == 0 ? 0 : .5f) + .004f + p.x * .492f, .004f + p.y * .492f);
        }
        static void Patch(BGSData data, Vector3 center, Vector3 right, Vector3 up,
            Vector3 normal, float width, float height, int seed, Color tint)
        {
            var random = new System.Random(seed); int tile = random.Next(2);
            float bend = R(random, .10f, .24f) * Mathf.Min(width, height);
            Vector3 ridge = center + normal * bend;
            var vertices = new Vector3[outline.Length]; var normals = new Vector3[outline.Length];
            var uv = new Vector2[outline.Length];
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = outline[i];
                float x = (p.x - .5f) * width, y = (p.y - .5f) * height;
                vertices[i] = center + right * x + up * y + normal * R(random, -.08f, .08f) * width;
                normals[i] = (normal + right * (p.x - .5f) * .35f + up * (p.y - .5f) * .35f).normalized;
                uv[i] = AtlasUV(p, tile);
            }
            for (int i = 0; i < outline.Length; i++)
            {
                int next = (i + 1) % outline.Length, start = data.vertices.Count;
                data.Triangle(ridge, vertices[i], vertices[next], AtlasUV(new Vector2(.5f, .5f), tile), uv[i], uv[next], tint, .65f);
                if (data.vertices.Count == start + 3)
                { data.normals[start] = normal; data.normals[start + 1] = normals[i]; data.normals[start + 2] = normals[next]; }
            }
        }
        static void Blade(BGSData data, Vector3 center, Vector3 axis, Vector3 normal, float length, float width, Color tint)
        {
            Vector3 side = Vector3.Cross(normal, axis).normalized, tip = center + axis * length;
            Vector3 left = center + axis * length * .43f - side * width * .5f;
            Vector3 right = center + axis * length * .43f + side * width * .5f;
            Vector3 ridge = center + axis * length * .48f + normal * width * .30f;
            data.Triangle(center, left, ridge, Vector2.zero, Vector2.up, Vector2.one, tint, .8f, true);
            data.Triangle(left, tip, ridge, Vector2.zero, Vector2.up, Vector2.one, tint, .8f, true);
            data.Triangle(tip, right, ridge, Vector2.zero, Vector2.up, Vector2.one, tint, .8f, true);
            data.Triangle(right, center, ridge, Vector2.zero, Vector2.up, Vector2.one, tint, .8f, true);
        }
        static void Flower(BGSData data, Vector3 center, Vector3 normal, float size, System.Random r, Color tint)
        {
            Vector3 right, up; Basis(normal, out right, out up); float phase = R(r, 0, Mathf.PI * 2);
            Color pink = Tint(new Color(R(r, .80f, 1), R(r, .08f, .28f), R(r, .40f, .80f)), tint);
            for (int i = 0; i < 3; i++)
            {
                float angle = phase + i * Mathf.PI * 2 / 3 + R(r, -.20f, .20f);
                Vector3 axis = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle) + normal * R(r, .1f, .4f)).normalized;
                Blade(data, center, axis, normal, size * R(r, .8f, 1.15f), size * .7f, pink);
            }
            Vector3 leaf = (right * Mathf.Cos(phase + 1) + up * Mathf.Sin(phase + 1) - normal * .2f).normalized;
            Blade(data, center - normal * size * .1f, leaf, normal, size * 1.3f, size * .65f,
                Tint(new Color(.20f, R(r, .35f, .48f), .12f), tint));
        }
        static void Cluster(BGSAuthoring t, BGSBuild result, Vector3 position, Vector3 axis,
            Vector3 outward, float size, float u, int seed, int lod)
        {
            var r = new System.Random(seed); axis = axis.normalized;
            if (axis.sqrMagnitude < .01f) axis = Vector3.down;
            Vector3 right = Vector3.ProjectOnPlane(outward, axis).normalized, across;
            if (right.sqrMagnitude < .01f) Basis(axis, out right, out across);
            across = Vector3.Cross(axis, right).normalized;
            float phase = R(r, -.45f, .45f); int patches = lod == 0 ? 4 : lod == 1 ? 3 : 2;
            // Keep two perpendicular patches even at LOD2. Positions and UVs
            // are identical to their counterparts in the higher LODs.
            for (int i = 0; i < patches; i++)
            {
                float angle = phase + i * 1.5707963f + (i >= 2 ? .4f : 0);
                Vector3 n = (right * Mathf.Cos(angle) + across * Mathf.Sin(angle)).normalized;
                Vector3 side = Vector3.Cross(axis, n).normalized;
                Vector3 up = (axis + n * R(r, -.32f, .32f)).normalized;
                n = Vector3.Cross(side, up).normalized;
                Vector3 center = position + n * size * R(r, -.12f, .12f) + axis * size * R(r, -.13f, .13f);
                Patch(result.edges, center, side, up, n, size * R(r, .85f, 1.15f), size * R(r, 1, 1.5f), Seed(seed, i + 11), t.tint);
            }
            bool nearSection = !t.lowerDetailOnly || (t.mode == BGSMode.FlowerWall
                ? u <= Mathf.Clamp(t.lowerDetailFraction, .1f, 1) : u >= 1 - Mathf.Clamp(t.lowerDetailFraction, .1f, 1));
            r = new System.Random(Seed(seed, 901));
            int flowers = Mathf.FloorToInt(Mathf.Clamp(t.detailDensity, 0, 2) * .8f + R(r, 0, 1));
            if (!nearSection || lod == 2) flowers = 0;
            if (lod == 1 && (seed & 3) != 0) flowers = 0;
            for (int i = 0; i < flowers; i++)
            {
                Vector3 n = (right + across * R(r, -.8f, .8f) + axis * R(r, -.3f, .3f)).normalized;
                Flower(result.body, position + n * size * .18f + axis * R(r, -.25f, .25f) * size,
                    n, Mathf.Min(.042f, size * .14f), r, t.tint);
            }
        }
        static void Stem(BGSData data, Vector3 a, Vector3 b, float radiusA, float radiusB, Color tint)
        {
            Vector3 delta = b - a; if (delta.sqrMagnitude < 1e-12f) return;
            Vector3 right, up; Basis(delta.normalized, out right, out up);
            Color brown = Tint(new Color(.26f, .20f, .12f), tint);
            for (int i = 0; i < 4; i++)
            {
                float x = i * Mathf.PI * .5f, y = (i + 1) * Mathf.PI * .5f;
                Vector3 d0 = right * Mathf.Cos(x) + up * Mathf.Sin(x), d1 = right * Mathf.Cos(y) + up * Mathf.Sin(y);
                data.Triangle(a + d0 * radiusA, b + d0 * radiusB, a + d1 * radiusA, Vector2.zero, Vector2.up, Vector2.right, brown, .05f, true);
                data.Triangle(b + d0 * radiusB, b + d1 * radiusB, a + d1 * radiusA, Vector2.up, Vector2.one, Vector2.right, brown, .05f, true);
            }
        }
        static void Branches(BGSAuthoring t, int lod, IBGSSurfaceQuery query, BGSBuild result)
        {
            if (t.branches.Count == 0) throw new InvalidOperationException("请先生成枝条曲线。");
            float density = Mathf.Clamp01(t.fullness), patchSize = PatchSize(t);
            for (int id = 0; id < t.branches.Count; id++)
            {
                var branch = t.branches[id];
                if (branch == null || branch.points == null || branch.points.Count < 2)
                    throw new InvalidOperationException("每条枝条至少需要两个控制点。");
                if (BGSCore.Length(t, BGSCore.Smooth(branch.points)) < .0001f) continue;
                var path = new Path(t, branch, query); if (path.length < .0001f) continue;
                int branchSeed = Seed(t.seed, id + 1);
                if (t.showStems)
                {
                    int steps = Mathf.Clamp(Mathf.CeilToInt(path.length / .20f), 2, 4096);
                    float radius = Mathf.Min(.009f, Mathf.Min(t.branchWidth, t.volumeDepth) * .018f);
                    for (int i = 0; i < steps; i++)
                        Stem(result.body, path.At(i / (float)steps).position, path.At((i + 1f) / steps).position,
                            radius * Mathf.Lerp(1, .2f, i / (float)steps), radius * Mathf.Lerp(1, .2f, (i + 1f) / steps), t.tint);
                }
                if (density <= 0) continue;
                // A narrow, leafy core joins the side shoots. It is still
                // made entirely of cutout leaves, never a filled sheath.
                int spineSites = Mathf.Max(2, Mathf.CeilToInt(path.length / Mathf.Clamp(patchSize * .45f, .10f, .25f)));
                if (spineSites > 24000) throw new InvalidOperationException("枝条过长，请分组生成。");
                for (int site = 0; site < spineSites; site++)
                {
                    int spineSeed = Seed(branchSeed, site + 17001); var r = new System.Random(spineSeed);
                    float u = (site + R(r, .22f, .78f)) / spineSites;
                    float chance = density * Mathf.Lerp(.96f, .65f + .31f * Activity(u * path.length, branchSeed), Mathf.Clamp01(t.gapAmount));
                    if (site != spineSites / 2 && R(r, 0, 1) > chance) continue;
                    Frame f = path.At(u); float angle = site * 2.3999632f;
                    Vector3 outward = f.right * Mathf.Cos(angle) + f.normal * Mathf.Sin(angle);
                    float size = patchSize * R(r, .48f, .72f) * Mathf.Lerp(.55f, 1, Envelope(u, u * path.length, branchSeed));
                    Cluster(t, result, f.position + outward * size * .08f, f.tangent, outward, size, u, spineSeed, lod);
                }
                float step = Mathf.Clamp(patchSize * .7f, .16f, .34f);
                int nodes = Mathf.Max(1, Mathf.CeilToInt(path.length / step));
                if (nodes > 12000) throw new InvalidOperationException("枝条过长，请分组生成。");
                for (int node = 0; node < nodes; node++)
                {
                    int nodeSeed = Seed(branchSeed, node + 1); var r = new System.Random(nodeSeed);
                    float u = (node + R(r, .15f, .85f)) / nodes;
                    float activity = Activity(u * path.length, branchSeed);
                    float chance = density * Mathf.Lerp(1, .18f + .82f * activity, Mathf.Clamp01(t.gapAmount));
                    if (node != nodes / 2 && R(r, 0, 1) > chance) continue;
                    if (node == nodes / 2 && density < .15f && R(r, 0, 1) > density * 6) continue;
                    Frame f = path.At(u); float envelope = Envelope(u, u * path.length, branchSeed);
                    float size = patchSize * R(r, .72f, 1.12f) * Mathf.Lerp(.40f, 1, envelope);
                    float angle = node * 2.3999632f + R(r, -.8f, .8f);
                    Vector3 radial = f.right * Mathf.Cos(angle) * t.branchWidth * .65f
                        + f.normal * Mathf.Sin(angle) * t.volumeDepth * .65f;
                    radial *= envelope * R(r, .65f, 1);
                    float drop = Mathf.Min(t.sideBranchLength * R(r, .45f, 1.25f), path.length * (1 - u) * .8f);
                    Vector3 a = f.position, b = a + radial * .75f + f.tangent * drop * .15f;
                    Vector3 c = a + radial + f.tangent * drop;
                    if (t.showStems)
                    {
                        float radius = Mathf.Min(.004f, patchSize * .012f);
                        for (int j = 0; j < 3; j++)
                            Stem(result.body, Bezier(a, b, c, j / 3f), Bezier(a, b, c, (j + 1f) / 3),
                                radius * (1 - j * .23f), radius * (1 - (j + 1) * .23f), t.tint);
                    }
                    int sites = Mathf.Clamp(Mathf.CeilToInt(drop / Mathf.Max(.09f, size * .7f)) + 1, 2, 7);
                    for (int site = 0; site < sites; site++)
                    {
                        float v = (site + R(r, .12f, .88f)) / sites;
                        if (site > 0 && R(r, 0, 1) > Mathf.Lerp(.55f, 1, density)) continue;
                        Vector3 position = Bezier(a, b, c, v) + f.normal * R(r, -.07f, .07f) * size;
                        Vector3 tangent = Vector3.Lerp(b - a, c - b, v).normalized;
                        if (tangent.sqrMagnitude < .01f) tangent = f.tangent;
                        Vector3 outward = radial.sqrMagnitude > 1e-10f ? radial.normalized : f.normal;
                        Cluster(t, result, position, tangent, outward, size * Mathf.Lerp(1, .45f, v), u, Seed(nodeSeed, site + 31), lod);
                    }
                }
            }
        }
        static void Wall(BGSAuthoring t, int lod, IBGSSurfaceQuery query, BGSBuild result)
        {
            if (t.wallPoints.Count != 9) throw new InvalidOperationException("花墙需要九个控制点。");
            float density = Mathf.Clamp01(t.fullness); if (density <= 0) return;
            float size = PatchSize(t), step = Mathf.Max(.06f, size * .55f);
            int nx = Mathf.Max(1, Mathf.CeilToInt(t.wallWidth / step)), ny = Mathf.Max(1, Mathf.CeilToInt(t.wallHeight / step));
            if ((long)nx * ny > 16000) throw new InvalidOperationException("花墙过大，请拆成几块生成。");
            // A fixed surface lattice avoids a physics query per flower.
            int gx = Mathf.Clamp(Mathf.CeilToInt(t.wallWidth / .35f), 1, 512);
            int gy = Mathf.Clamp(Mathf.CeilToInt(t.wallHeight / .35f), 1, 512);
            var grid = new Vector3[gx + 1, gy + 1];
            for (int y = 0; y <= gy; y++) for (int x = 0; x <= gx; x++)
                grid[x, y] = BGSCore.WallPoint(t, x / (float)gx, y / (float)gy, query);
            for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
            {
                int seed = Seed(t.seed, y * nx + x + 3001); var r = new System.Random(seed);
                float u = (x + R(r, .08f, .92f)) / nx, v = (y + R(r, .08f, .92f)) / ny;
                float activity = Activity(u * t.wallWidth + v * t.wallHeight * .62f, t.seed);
                float chance = density * Mathf.Lerp(1, .12f + .88f * activity, Mathf.Clamp01(t.gapAmount));
                if ((x != nx / 2 || y != ny / 2) && R(r, 0, 1) > chance) continue;
                float xx = u * gx, yy = v * gy;
                int ix = Mathf.Min(gx - 1, Mathf.FloorToInt(xx)), iy = Mathf.Min(gy - 1, Mathf.FloorToInt(yy));
                float tx = xx - ix, ty = yy - iy;
                Vector3 dx = Vector3.Lerp(grid[ix + 1, iy] - grid[ix, iy], grid[ix + 1, iy + 1] - grid[ix, iy + 1], ty);
                Vector3 dy = Vector3.Lerp(grid[ix, iy + 1] - grid[ix, iy], grid[ix + 1, iy + 1] - grid[ix + 1, iy], tx);
                Vector3 normal = Vector3.Cross(dx, dy).normalized;
                if (normal.sqrMagnitude < .01f) normal = Vector3.forward;
                if (Vector3.Dot(normal, Vector3.forward) < 0) normal = -normal;
                Vector3 position = Vector3.Lerp(Vector3.Lerp(grid[ix, iy], grid[ix + 1, iy], tx), Vector3.Lerp(grid[ix, iy + 1], grid[ix + 1, iy + 1], tx), ty);
                float depth = t.volumeDepth * R(r, .08f, .92f) * Mathf.Lerp(.4f, 1, activity);
                Vector3 axis = (dy.normalized + dx.normalized * R(r, -.7f, .7f)).normalized;
                if (axis.sqrMagnitude < .01f) axis = Vector3.up;
                Cluster(t, result, position + normal * depth, axis, normal, size * R(r, .65f, 1.25f), v, seed, lod);
            }
        }
        public static BGSBuild Build(BGSAuthoring t, int lod, IBGSSurfaceQuery query)
        {
            var result = new BGSBuild();
            if (t.mode == BGSMode.FlowerWall) Wall(t, lod, query, result); else Branches(t, lod, query, result);
            return result;
        }
    }
}
#endif
