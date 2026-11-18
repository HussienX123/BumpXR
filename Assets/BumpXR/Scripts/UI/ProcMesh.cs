using System.Collections.Generic;
using UnityEngine;

namespace BumpXR.UI
{
    /// <summary>Tiny vertex-colored mesh builder used by the procedural Demo Buddies (no imported art).</summary>
    public sealed class ProcMesh
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<int> _t = new List<int>();

        public int TriangleCount => _t.Count / 3;
        public int VertexCount => _v.Count;

        private int Vert(Vector3 p, Vector3 n, Color32 c)
        {
            _v.Add(p);
            _n.Add(n);
            _c.Add(c);
            return _v.Count - 1;
        }

        /// <summary>Ellipsoid (sphere when radii are equal). Outward winding.</summary>
        public void AddEllipsoid(Vector3 center, Vector3 radii, Color color, int lon = 12, int lat = 8)
        {
            Color32 c32 = color;
            int baseIndex = _v.Count;
            for (int y = 0; y <= lat; y++)
            {
                float phi = y / (float)lat * Mathf.PI;
                float cy = Mathf.Cos(phi), sr = Mathf.Sin(phi);
                for (int x = 0; x <= lon; x++)
                {
                    float th = x / (float)lon * Mathf.PI * 2f;
                    var dir = new Vector3(sr * Mathf.Sin(th), cy, sr * Mathf.Cos(th));
                    var n = new Vector3(dir.x / radii.x, dir.y / radii.y, dir.z / radii.z).normalized;
                    Vert(center + Vector3.Scale(dir, radii), n, c32);
                }
            }

            int stride = lon + 1;
            for (int y = 0; y < lat; y++)
            {
                for (int x = 0; x < lon; x++)
                {
                    int a = baseIndex + y * stride + x, b = a + 1, c = a + stride, d = c + 1;
                    _t.Add(a); _t.Add(c); _t.Add(b);
                    _t.Add(b); _t.Add(c); _t.Add(d);
                }
            }
        }

        public void AddSphere(Vector3 center, float radius, Color color, int lon = 10, int lat = 7) =>
            AddEllipsoid(center, Vector3.one * radius, color, lon, lat);

        /// <summary>Tapered tube from a to b (outward winding, no end caps).</summary>
        public void AddTube(Vector3 a, Vector3 b, float r0, float r1, Color color, int sides = 10)
        {
            Color32 c32 = color;
            Vector3 w = (b - a).normalized;
            Vector3 helper = Mathf.Abs(w.y) < 0.99f ? Vector3.up : Vector3.right;
            Vector3 u = Vector3.Cross(helper, w).normalized;
            Vector3 v = Vector3.Cross(w, u);
            int baseIndex = _v.Count;
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 center = ring == 0 ? a : b;
                float r = ring == 0 ? r0 : r1;
                for (int i = 0; i <= sides; i++)
                {
                    float th = i / (float)sides * Mathf.PI * 2f;
                    Vector3 radial = Mathf.Cos(th) * u + Mathf.Sin(th) * v;
                    Vert(center + radial * r, radial, c32);
                }
            }

            int stride = sides + 1;
            for (int i = 0; i < sides; i++)
            {
                int p0 = baseIndex + i, p0n = p0 + 1, p1 = p0 + stride, p1n = p1 + 1;
                _t.Add(p0); _t.Add(p0n); _t.Add(p1);
                _t.Add(p1); _t.Add(p0n); _t.Add(p1n);
            }
        }

        /// <summary>Flat-shaded box, optionally rotated about its center.</summary>
        public void AddBox(Vector3 center, Vector3 size, Color color, Quaternion? rotation = null)
        {
            Color32 c32 = color;
            Quaternion rot = rotation ?? Quaternion.identity;
            Vector3 h = size * 0.5f;
            // face normal, u, v with u x v = normal
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] us = { Vector3.up, Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.up };
            Vector3[] vs = { Vector3.forward, Vector3.up, Vector3.right, Vector3.forward, Vector3.up, Vector3.right };
            for (int f = 0; f < 6; f++)
            {
                Vector3 nn = n[f], uu = us[f], vv = vs[f];
                Vector3 c = Vector3.Scale(nn, h);
                Vector3 du = Vector3.Scale(uu, h), dv = Vector3.Scale(vv, h);
                Vector3 nWorld = rot * nn;
                int i0 = Vert(center + rot * (c - du - dv), nWorld, c32);
                int i1 = Vert(center + rot * (c + du - dv), nWorld, c32);
                int i2 = Vert(center + rot * (c + du + dv), nWorld, c32);
                int i3 = Vert(center + rot * (c - du + dv), nWorld, c32);
                _t.Add(i0); _t.Add(i1); _t.Add(i2);
                _t.Add(i0); _t.Add(i2); _t.Add(i3);
            }
        }

        /// <summary>Torus around <paramref name="axis"/>. Vertices are ordered ring-by-ring (major segment) so a fill can be painted along the loop.</summary>
        public void AddTorus(Vector3 center, Vector3 axis, float majorRadius, float minorRadius, Color color, int major = 40, int minor = 6)
        {
            Color32 c32 = color;
            Vector3 w = axis.normalized;
            Vector3 helper = Mathf.Abs(w.y) < 0.99f ? Vector3.up : Vector3.right;
            Vector3 u = Vector3.Cross(helper, w).normalized;
            Vector3 v = Vector3.Cross(w, u);
            int baseIndex = _v.Count;
            for (int i = 0; i <= major; i++)
            {
                float al = i / (float)major * Mathf.PI * 2f;
                Vector3 radial = Mathf.Cos(al) * u + Mathf.Sin(al) * v;
                for (int j = 0; j <= minor; j++)
                {
                    float be = j / (float)minor * Mathf.PI * 2f;
                    Vector3 d = Mathf.Cos(be) * radial + Mathf.Sin(be) * w;
                    Vert(center + radial * majorRadius + d * minorRadius, d, c32);
                }
            }

            int stride = minor + 1;
            for (int i = 0; i < major; i++)
            {
                for (int j = 0; j < minor; j++)
                {
                    int a = baseIndex + i * stride + j, b = a + stride, c = a + 1, d = b + 1;
                    _t.Add(a); _t.Add(b); _t.Add(c);
                    _t.Add(b); _t.Add(d); _t.Add(c);
                }
            }
        }

        /// <summary>Recolor vertices [first, first+count) (used for the progress ring fill).</summary>
        public static void Paint(Color32[] colors, int first, int count, Color32 color)
        {
            for (int i = first; i < first + count && i < colors.Length; i++) colors[i] = color;
        }

        public Color32[] CopyColors() => _c.ToArray();

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetColors(_c);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            m.UploadMeshData(false);
            return m;
        }
    }
}

