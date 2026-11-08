using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class RandomCurvedPathChecks
{
    [MenuItem("Tools/Mission Marshal/Check Random Curved Path")]
    public static void Run()
    {
        int checks = 0;
        Action<bool, string> expect = (ok, label) =>
        { checks++; if (!ok) throw new InvalidOperationException("Path check: " + label); };
        var root = new GameObject("Temporary path checks");
        try
        {
            root.transform.SetPositionAndRotation(new Vector3(17, -3, 8), Quaternion.Euler(20, 35, 10));
            var unrelated = new GameObject("Unrelated child");
            unrelated.transform.SetParent(root.transform);
            var path = root.AddComponent<RandomCurvedPath>();
            Action<string, object> set = (name, value) => typeof(RandomCurvedPath)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(path, value);
            expect(path.GetPosition(0.5f) == root.transform.position, "ungenerated path returns origin");
            set("randomizeSeed", false);
            set("seed", 481);
            set("samplesPerSegment", 128);
            path.GeneratePath(12);
            expect(path.WaypointCount == 12 && path.GeneratedObjects.Count == 12, "requested placeholder count");
            float previous = 0;
            var saved = new List<Vector3>();
            var oldObjects = new List<GameObject>(path.GeneratedObjects);
            foreach (GameObject item in path.GeneratedObjects)
            {
                expect(item.GetComponents<Component>().Length == 1, "placeholder contains only Transform");
                expect(item.transform.parent == root.transform, "placeholder parent");
                float radius = Vector3.Distance(root.transform.position, item.transform.position);
                expect(radius > previous, "strictly increasing radial distances");
                expect(Math.Abs(Vector3.Dot(item.transform.position - root.transform.position, root.transform.up)) < 0.001f,
                    "points use rotated local XZ plane");
                previous = radius;
                saved.Add(item.transform.position);
            }
            path.GeneratePath(12);
            for (int i = 0; i < saved.Count; i++)
            {
                expect(oldObjects[i] == null, "regeneration deletes previous owned placeholders");
                expect(Vector3.Distance(saved[i], path.GeneratedObjects[i].transform.position) < 0.001f, "seed reproducibility");
            }
            expect(unrelated != null && root.transform.childCount == 13, "unrelated child preserved without accumulating placeholders");
            expect(Vector3.Distance(path.GetPosition(0), root.transform.position) < 0.001f, "start endpoint");
            expect(Vector3.Distance(path.GetPosition(1), saved[11]) < 0.001f, "end endpoint");
            expect(path.GetPosition(-1) == path.GetPosition(0) && path.GetPosition(2) == path.GetPosition(1), "clamp progress");
            expect(path.GetPosition(float.NaN) == path.GetPosition(0), "invalid progress fallback");
            expect(path.TotalLength > previous, "curved length exceeds endpoint distance");
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                Vector3 centre = path.GetPosition(t);
                Quaternion rotation = path.GetRotation(t);
                expect(Vector3.Dot(rotation * Vector3.forward, -path.GetTangent(t)) > 0.9999f,
                    "rotation faces reversed path tangent at all progress values");
                expect(Vector3.Dot(rotation * Vector3.up, root.transform.up) > 0.9999f,
                    "rotation preserves path-plane up direction");
                Vector3 offset = path.GetOffsetPosition(t, 10, 57) - centre;
                expect(Math.Abs(offset.magnitude - 10) < 0.001f, "offset radius");
                expect(Math.Abs(Vector3.Dot(offset, path.GetTangent(t))) < 0.001f, "offset perpendicular to curve");
                Vector3 opposite = path.GetOffsetPosition(t, 10, 237) - centre;
                expect((offset + opposite).magnitude < 0.001f, "opposite ring angles");
                expect(path.GetOffsetPosition(t, 0, 90) == centre, "zero-radius offset");
            }
            path.GeneratePath(3);
            expect(path.WaypointCount == 3 && path.GeneratedObjects.Count == 3, "regeneration with smaller count");
            path.GeneratePath(0);
            expect(path.WaypointCount == 0 && path.TotalLength == 0 && root.transform.childCount == 1, "zero count clears owned placeholders");
            path.GeneratePath(-4);
            expect(path.GeneratedObjects.Count == 0, "negative count treated as zero");
            set("objectCount", 5);
            path.GeneratePath();
            expect(path.WaypointCount == 5, "Inspector count used by parameterless generation");
            var templateA = new GameObject("Template A");
            var templateB = new GameObject("Template B");
            templateA.transform.SetParent(root.transform);
            templateB.transform.SetParent(root.transform);
            set("pathObjectPrefabs", new List<GameObject> { templateA, null, root, templateB });
            set("pathObjectSpawnCount", 30);
            path.GeneratePath(5);
            expect(path.SpawnedPathObjects.Count == 30 && path.WaypointCount == 5,
                "spawn count independent of control-point count");
            var oldSpawns = new List<GameObject>(path.SpawnedPathObjects);
            var spawnPositions = new List<Vector3>();
            for (int i = 0; i < oldSpawns.Count; i++)
            {
                float progress = path.SpawnedPathProgress[i];
                expect(progress > (i == 0 ? 0 : path.SpawnedPathProgress[i - 1]) && progress < 1,
                    "increasing spaced progress remains inside path endpoints");
                Vector3 position = oldSpawns[i].transform.position;
                expect(oldSpawns[i].name.StartsWith("Template A") || oldSpawns[i].name.StartsWith("Template B"),
                    "spawn uses a valid prefab");
                expect(Math.Abs(Vector3.Dot(position - path.GetPosition(progress), path.GetTangent(progress))) < 0.001f,
                    "spawn remains in perpendicular path ring");
                float offset = Vector3.Distance(position, path.GetPosition(progress));
                expect(offset >= 1.499f && offset <= 3.001f, "default spawn radius range");
                spawnPositions.Add(position);
            }
            path.GeneratePath(5);
            for (int i = 0; i < oldSpawns.Count; i++)
            {
                expect(oldSpawns[i] == null, "regeneration removes old decorations");
                expect(Vector3.Distance(spawnPositions[i], path.SpawnedPathObjects[i].transform.position) < 0.001f,
                    "seeded decoration placement reproducible");
            }
            expect(templateA != null && templateB != null && unrelated != null, "spawn templates/unrelated children preserved");
            set("pathObjectSpacingVariation", 0f);
            set("pathObjectSpawnCount", 100);
            path.GeneratePath(2);
            expect(path.SpawnedPathObjects.Count == 100, "all requested objects fit even on a short path");
            for (int i = 0; i < 100; i++)
                expect(Math.Abs(path.SpawnedPathProgress[i] - (i + 1f) / 101f) < 0.000001f,
                    "zero variation gives equal distance spacing");
            set("pathObjectSpawnCount", 200);
            set("pathObjectRadius", new Vector2(10, 30));
            set("pathObjectMinimumSeparation", 2f);
            set("radialStep", new Vector2(50, 50));
            set("maxTurnDegrees", 0f);
            path.GeneratePath(2);
            expect(path.SpawnedPathObjects.Count == 200, "dense population preserves count");
            for (int i = 0; i < path.SpawnedPathObjects.Count; i++)
            {
                Vector3 position = path.SpawnedPathObjects[i].transform.position;
                float radius = Vector3.Distance(position, path.GetPosition(path.SpawnedPathProgress[i]));
                expect(radius >= 9.999f && radius <= 30.001f, "wide annulus respects radius range");
                for (int j = 0; j < i; j++)
                    expect(Vector3.Distance(position, path.SpawnedPathObjects[j].transform.position) >= 1.999f,
                        "nearby rings respect world-space separation");
            }
            set("pathObjectRadius", Vector2.zero);
            set("pathObjectSpawnCount", 5);
            set("pathObjectMinimumSeparation", 1000f);
            path.GeneratePath(2);
            expect(path.SpawnedPathObjects.Count == 5, "impossible spacing uses bounded fallback and preserves count");
            path.GeneratePath(0);
            expect(path.SpawnedPathObjects.Count == 0, "empty path clears decorations without spawning");
            Debug.Log($"RandomCurvedPath checks passed: {checks}.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
