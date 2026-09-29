using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SashimiBoy.EditorTools
{
    // Builds project-owned geometry while preserving the source mesh, materials and UVs.
    public static class GeneratedMeshCrop
    {
        private struct Vertex
        {
            public Vector3 position, normal;
            public Vector2 uv;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
            {
                position = Vector3.LerpUnclamped(a.position, b.position, t),
                normal = Vector3.LerpUnclamped(a.normal, b.normal, t).normalized,
                uv = Vector2.LerpUnclamped(a.uv, b.uv, t)
            };
        }

        public static Mesh Crop(Mesh source, Matrix4x4 matrix, Bounds crop, string name)
        {
            var vertices = source.vertices;
            var normals = source.normals;
            var uvs = source.uv;
            var input = new Vertex[vertices.Length];
            var normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < input.Length; i++) input[i] = new Vertex
            {
                position = matrix.MultiplyPoint3x4(vertices[i]),
                normal = normalMatrix.MultiplyVector(normals.Length == vertices.Length ? normals[i] : Vector3.up).normalized,
                uv = uvs.Length == vertices.Length ? uvs[i] : Vector2.zero
            };
            var points = new List<Vector3>(); var ns = new List<Vector3>(); var tex = new List<Vector2>();
            var submeshes = new List<int[]>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] triangles = source.GetTriangles(sub);
                var indices = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vertex a = input[triangles[i]], b = input[triangles[i + 1]], c = input[triangles[i + 2]];
                    Vector3 min = Vector3.Min(a.position, Vector3.Min(b.position, c.position));
                    Vector3 max = Vector3.Max(a.position, Vector3.Max(b.position, c.position));
                    if (max.x < crop.min.x || min.x > crop.max.x || max.y < crop.min.y || min.y > crop.max.y || max.z < crop.min.z || min.z > crop.max.z) continue;
                    var polygon = new List<Vertex> { a, b, c };
                    if (!crop.Contains(a.position) || !crop.Contains(b.position) || !crop.Contains(c.position))
                        for (int axis = 0; axis < 3 && polygon.Count > 0; axis++)
                        {
                            polygon = Clip(polygon, axis, crop.min[axis], true);
                            polygon = Clip(polygon, axis, crop.max[axis], false);
                        }
                    for (int k = 1; k + 1 < polygon.Count; k++)
                        foreach (var v in new[] { polygon[0], polygon[k], polygon[k + 1] })
                        {
                            indices.Add(points.Count); points.Add(v.position); ns.Add(v.normal); tex.Add(v.uv);
                        }
                }
                submeshes.Add(indices.ToArray());
            }
            var result = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            result.SetVertices(points); result.SetNormals(ns); result.SetUVs(0, tex);
            result.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) result.SetTriangles(submeshes[i], i);
            result.RecalculateBounds();
            return result;
        }

        private static List<Vertex> Clip(List<Vertex> input, int axis, float plane, bool above)
        {
            var output = new List<Vertex>();
            for (int i = 0; i < input.Count; i++)
            {
                Vertex a = input[i], b = input[(i + 1) % input.Count];
                bool ai = above ? a.position[axis] >= plane : a.position[axis] <= plane;
                bool bi = above ? b.position[axis] >= plane : b.position[axis] <= plane;
                if (ai) output.Add(a);
                if (ai != bi) output.Add(Vertex.Lerp(a, b, (plane - a.position[axis]) / (b.position[axis] - a.position[axis])));
            }
            return output;
        }
    }
}
