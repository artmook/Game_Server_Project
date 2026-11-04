using UnityEngine;
using UnityEngine.AI;

namespace GameServer
{
    public static class NavMeshRandom
    {
        private static Vector3[] vertices;
        private static int[] indices;
        private static float[] cumulativeAreas;
        private static float totalArea;

        public static bool Build()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            vertices = tri.vertices;
            indices = tri.indices;

            int triCount = indices.Length / 3;
            cumulativeAreas = new float[triCount];
            totalArea = 0f;

            for (int i = 0; i < triCount; i++)
            {
                Vector3 a = vertices[indices[i * 3]];
                Vector3 b = vertices[indices[i * 3 + 1]];
                Vector3 c = vertices[indices[i * 3 + 2]];
                totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                cumulativeAreas[i] = totalArea;
            }

            if (triCount == 0) Debug.LogError("NavMesh ¾øÀ½");
            return triCount > 0;
        }

        public static bool TryGetRandomPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (cumulativeAreas == null || totalArea <= 0f) return false;

            float r = Random.value * totalArea;
            int lo = 0, hi = cumulativeAreas.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (cumulativeAreas[mid] < r) lo = mid + 1; else hi = mid;
            }

            Vector3 a = vertices[indices[lo * 3]];
            Vector3 b = vertices[indices[lo * 3 + 1]];
            Vector3 c = vertices[indices[lo * 3 + 2]];

            float u = Random.value, v = Random.value;
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            point = a + u * (b - a) + v * (c - a);
            return true;
        }

        public static bool TryGetRandomPointNear(Vector3 center, float radius, out Vector3 point, int attempts = 10)
        {
            for (int i = 0; i < attempts; i++)
            {
                Vector2 o = Random.insideUnitCircle * radius;
                Vector3 candidate = center + new Vector3(o.x, 0f, o.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }

            if (TryGetRandomPoint(out point))
            {
                return true;
            }

            point = center;
            return false;
        }
    }
}
