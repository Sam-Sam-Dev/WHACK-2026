/*
 * QUICK USE
 *   GeneratePath()                          -> create empty path placeholders
 *   GeneratePath(count)                     -> create exactly count empty placeholders
 * Object Count in the Inspector controls generation; 0 creates an empty path.
 *   GetPosition(progress)                   -> world position, progress clamped 0..1
 *   GetOffsetPosition(progress,radius,angle) -> point on a ring around that position
 *   GetTangent(progress)                    -> forward direction along the curve
 *   GetRotation(progress)                   -> quaternion facing forward along the curve
 * Inspector context menu: Generate Path. Enable Gizmos in the Scene view.
 *
 * PATH RULES
 * The starting point is progress 0, followed by generated empty GameObjects.
 * Each placeholder's distance from the starting point increases by a
 * random Radial Step. Positions lie in the start transform's local XZ plane;
 * its forward direction sets the initial heading. Increasing distance applies to
 * the placed waypoints; the interpolated curve may bend inward between them.
 * Max Turn Degrees controls random heading changes. Curvature scales the smooth
 * Hermite tangents: 0 = straight segments, 1 = full smoothing through the points.
 * A fixed seed reproduces placements; Randomize Seed makes each generation new.
 *
 * SAMPLING
 * Progress is approximate DISTANCE along the curve, not a waypoint index. A
 * sampled length table keeps motion roughly uniform; Samples Per Segment sets
 * accuracy. Ring offsets are perpendicular to the local tangent, angle in degrees:
 * 0=right, 90=up, 180=left, 270=down relative to the path's starting up direction.
 * Radius is world units. Empty waypoints define the curve; Path Object Prefabs
 * optionally spawn decorations at increasing distances, with random gaps, radius
 * and ring angle. Gaps are fitted to the path length so the requested count fits.
 * Regeneration replaces this generator's waypoints and spawned decorations only.
 * Regenerate after moving the start or changing settings.
 * Gizmos and sampling use the same generated world-space curve.
 */
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RandomCurvedPath : MonoBehaviour
{
    [Header("Destination")]
    public GameObject destinationPrefab;
    [Min(0)] public float destinationOffset = 20f;
    [SerializeField, HideInInspector] private GameObject destinationInstance;
    public GameObject DestinationInstance => destinationInstance;
    [Header("Placement")]
    [Tooltip("Number of empty waypoint GameObjects to create, excluding the starting point.")]
    [SerializeField, Min(0)] private int objectCount = 20;
    [SerializeField, HideInInspector] private List<GameObject> generatedObjects = new List<GameObject>();
    public IReadOnlyList<GameObject> GeneratedObjects => generatedObjects;
    [Tooltip("Defaults to this transform. Its position is the origin; local XZ is the placement plane.")]
    [SerializeField] private Transform startingPoint;
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private Vector2 radialStep = new Vector2(20f, 40f);
    [SerializeField, Range(0f, 150f)] private float maxTurnDegrees = 30f;
    [SerializeField, Range(0f, 1f)] private float curvature = 0.75f;
    [Header("Objects around the path")]
    [Tooltip("Randomly pick one prefab for each spawn. Null entries are ignored.")]
    [SerializeField] private List<GameObject> pathObjectPrefabs = new List<GameObject>();
    [SerializeField, Min(0)] private int pathObjectSpawnCount = 20;
    [Tooltip("0 gives equal distance spacing; larger values randomise the gaps while keeping the exact spawn count.")]
    [SerializeField, Range(0f, 0.9f)] private float pathObjectSpacingVariation = 0.35f;
    [Tooltip("Minimum/maximum distance from the sampled path point, in world units.")]
    [SerializeField] private Vector2 pathObjectRadius = new Vector2(1.5f, 3f);
    [Tooltip("Preferred minimum WORLD distance between spawned object centres, including neighboring rings. Increase for larger planets.")]
    [SerializeField, Min(0f)] private float pathObjectMinimumSeparation = 2f;
    [Tooltip("Try different ring angles/radii before using the most separated candidate. Count is preserved if space is too crowded.")]
    [SerializeField, Range(1, 128)] private int pathObjectPlacementAttempts = 32;
    [SerializeField, HideInInspector] private List<GameObject> spawnedPathObjects = new List<GameObject>();
    public IReadOnlyList<GameObject> SpawnedPathObjects => spawnedPathObjects;
    [SerializeField, HideInInspector] private List<float> spawnedPathProgress = new List<float>();
    public IReadOnlyList<float> SpawnedPathProgress => spawnedPathProgress;
    [Header("Randomness and sampling")]
    [SerializeField] private bool randomizeSeed = true;
    [SerializeField] private int seed = 12345;
    [SerializeField, Range(8, 128)] private int samplesPerSegment = 32;
    [Header("Gizmos")]
    [SerializeField] private Color pathColor = Color.cyan;
    [SerializeField] private float waypointGizmoRadius = 0.6f;

    // Persist the generated curve through editor/domain reloads without moving
    // objects during OnValidate or OnDrawGizmos. Samples are disposable derived data.
    [SerializeField, HideInInspector] private List<Vector3> points = new List<Vector3>();
    [SerializeField, HideInInspector] private Vector3 pathUp = Vector3.up;
    private readonly List<float> sampleDistances = new List<float>();
    private readonly List<Vector3> samplePositions = new List<Vector3>();
    private bool samplesDirty = true;
    public int WaypointCount => Mathf.Max(0, points.Count - 1);
    public float TotalLength { get { EnsureSamples(); return sampleDistances.Count == 0 ? 0 : sampleDistances[sampleDistances.Count - 1]; } }

    private void Start() { if (generateOnStart) GeneratePath(); }
    private void OnValidate() { samplesDirty = true; }

    [ContextMenu("Generate Path")]
    public void GeneratePath() => GeneratePath(objectCount);

    /// <summary>Replace this generator's placeholders with count new empty GameObjects.</summary>
    public void GeneratePath(int count)
    {
        int limit = Mathf.Max(0, count);
        Transform origin = startingPoint != null ? startingPoint : transform;
        // Do not destroy the starting transform if it was assigned to a previous
        // generated waypoint. Use an independent scene transform as the start.
        foreach (GameObject item in generatedObjects)
            if (item != null && origin.IsChildOf(item.transform))
            {
                Debug.LogWarning("Starting Point must not be one of this path's generated placeholders.", this);
                return;
            }
        foreach (GameObject item in spawnedPathObjects)
            if (item != null && origin.IsChildOf(item.transform))
            {
                Debug.LogWarning("Starting Point must not be a spawned path object.", this);
                return;
            }
        Vector3 start = origin.position;
        Vector3 forward = origin.forward.normalized;
        Vector3 right = origin.right.normalized;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.Undo.RecordObject(this, "Generate curved path");
#endif
        if (destinationInstance != null) ClearGeneratedList(new List<GameObject> { destinationInstance });
        destinationInstance = null;
        ClearGeneratedObjects();
        ClearGeneratedList(spawnedPathObjects);
        spawnedPathProgress.Clear();
        pathUp = origin.up.normalized;
        points.Clear();
        points.Add(start);
        var random = new System.Random(randomizeSeed ? Guid.NewGuid().GetHashCode() : seed);
        float minimum = Mathf.Max(0.01f, Finite(radialStep.x, 20f));
        float maximum = Mathf.Max(minimum, Finite(radialStep.y, minimum));
        float turnLimit = Mathf.Clamp(Finite(maxTurnDegrees, 30f), 0f, 150f);
        float distance = 0f;
        float angle = 0f;
        for (int i = 0; i < limit; i++)
        {
            distance += Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
            angle += Mathf.Lerp(-turnLimit, turnLimit, (float)random.NextDouble()) * Mathf.Deg2Rad;
            Vector3 position = start + distance * (forward * Mathf.Cos(angle) + right * Mathf.Sin(angle));
            var placeholder = new GameObject($"Path Point {i + 1:000}");
            placeholder.transform.SetParent(transform, false);
            placeholder.transform.position = position;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(placeholder, "Create path placeholder");
#endif
            generatedObjects.Add(placeholder);
            points.Add(position);
        }
        samplesDirty = true;
        EnsureSamples();
        SpawnObjectsAroundPath(random);
        if (points.Count > 1 && destinationPrefab != null)
        {
            destinationInstance = Instantiate(destinationPrefab, GetPosition(1) + GetTangent(1) * Mathf.Max(0, destinationOffset), destinationPrefab.transform.rotation, transform);
            destinationInstance.name = "Destination Planet";
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(destinationInstance, "Create destination planet");
#endif
        }
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }

    // Uses the same seeded random stream as the waypoints. Create N+1 positive
    // random distance gaps, then scale their sum to the path's full length. The
    // first N gaps end at spawn points; the final gap leaves room before the end.
    // No overshoot/early stopping, so every requested spawn fits on a valid path.
    // Radius/angle sample the perpendicular ring independently of these gaps.
    // Decorations do not become control points or alter the generated curve.
    private void SpawnObjectsAroundPath(System.Random random)
    {
        if (points.Count < 2 || pathObjectPrefabs == null) return;
        var prefabs = new List<GameObject>();
        foreach (GameObject prefab in pathObjectPrefabs)
        {
            // A prefab containing this generator could recursively spawn paths.
            if (prefab != null && prefab.GetComponentInChildren<RandomCurvedPath>(true) == null)
                prefabs.Add(prefab);
        }
        if (prefabs.Count == 0) return;
        int count = Mathf.Max(0, pathObjectSpawnCount);
        if (count == 0 || TotalLength <= 0f) return;
        float variation = Mathf.Clamp(Finite(pathObjectSpacingVariation, 0.35f), 0f, 0.9f);
        var gaps = new double[count + 1];
        double totalGapWeight = 0;
        for (int i = 0; i < gaps.Length; i++)
        {
            gaps[i] = 1.0 - variation + random.NextDouble() * 2.0 * variation;
            totalGapWeight += gaps[i];
        }
        double distanceWeight = 0;
        float first = Mathf.Max(0f, Finite(pathObjectRadius.x, 1.5f));
        float second = Mathf.Max(0f, Finite(pathObjectRadius.y, 3f));
        float minimum = Mathf.Min(first, second), maximum = Mathf.Max(first, second);
        float separation = Mathf.Max(0f, Finite(pathObjectMinimumSeparation, 2f));
        int attempts = Mathf.Clamp(pathObjectPlacementAttempts, 1, 128);
        // Spatial buckets avoid comparing each candidate to the entire map.
        var occupied = new Dictionary<Vector3Int, List<Vector3>>();
        int crowdedPlacements = 0;
        for (int i = 0; i < count; i++)
        {
            distanceWeight += gaps[i];
            // GetPosition/GetOffsetPosition interpret progress as arc length.
            float progress = (float)(distanceWeight / totalGapWeight);
            GameObject prefab = prefabs[random.Next(prefabs.Count)];
            Vector3 position = FindSpawnPosition(progress, minimum, maximum, i, separation, attempts,
                random, occupied, out bool crowded);
            if (crowded) crowdedPlacements++;
            if (separation > 0f)
            {
                Vector3Int cell = SpawnCell(position, separation);
                if (!occupied.TryGetValue(cell, out var neighbours)) occupied[cell] = neighbours = new List<Vector3>();
                neighbours.Add(position);
            }
            GameObject instance;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
                instance.transform.SetPositionAndRotation(position, prefab.transform.rotation);
            }
            else
#endif
                instance = Instantiate(prefab, position, prefab.transform.rotation, transform);
            instance.name = $"{prefab.name} - Path Spawn {i + 1:000}";
            spawnedPathObjects.Add(instance);
            spawnedPathProgress.Add(progress);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.Undo.RegisterCreatedObjectUndo(instance, "Spawn path object");
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            }
#endif
        }
        if (crowdedPlacements > 0)
            Debug.LogWarning($"Path population: {crowdedPlacements}/{count} objects used best-effort spacing. " +
                "Increase Path Object Radius or reduce Minimum Separation/count for more room.", this);
    }

    // Golden-angle turns spread consecutive objects around the path, rather than
    // stacking on one side. Uniform area sampling fills wide annuli proportionally:
    // larger radius ranges populate the surrounding map, not just a thin ring.
    // Nearby-ring conflicts are caught by world-space separation, even at bends.
    private Vector3 FindSpawnPosition(float progress, float minimum, float maximum, int index,
        float separation, int attempts, System.Random random,
        Dictionary<Vector3Int, List<Vector3>> occupied, out bool crowded)
    {
        float bestClearance = -1f;
        Vector3 bestPosition = Vector3.zero;
        float requiredSquared = separation * separation;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float radius = Mathf.Sqrt(Mathf.Lerp(minimum * minimum, maximum * maximum, (float)random.NextDouble()));
            float angle = (index + attempt) * 137.50776f + (float)random.NextDouble() * 30f;
            Vector3 candidate = GetOffsetPosition(progress, radius, angle);
            float clearance = requiredSquared;
            if (separation > 0f)
            {
                Vector3Int cell = SpawnCell(candidate, separation);
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                            if (occupied.TryGetValue(cell + new Vector3Int(x, y, z), out var neighbours))
                                foreach (Vector3 other in neighbours)
                                    clearance = Mathf.Min(clearance, (candidate - other).sqrMagnitude);
            }
            if (clearance > bestClearance) { bestClearance = clearance; bestPosition = candidate; }
            if (clearance >= requiredSquared) { crowded = false; return candidate; }
        }
        // Bounded retries: never hang or silently drop objects in a crowded map.
        crowded = true;
        return bestPosition;
    }

    private static Vector3Int SpawnCell(Vector3 position, float size) => new Vector3Int(
        Mathf.FloorToInt(position.x / size), Mathf.FloorToInt(position.y / size), Mathf.FloorToInt(position.z / size));

    /// <summary>World position at a clamped fraction of the total curved path length.</summary>
    public Vector3 GetPosition(float progress)
    {
        if (points.Count == 0) return startingPoint != null ? startingPoint.position : transform.position;
        if (points.Count == 1) return points[0];
        return Evaluate(ParameterAtDistance(progress));
    }

    public Vector3 GetTangent(float progress)
    {
        if (points.Count < 2) return startingPoint != null ? startingPoint.forward : transform.forward;
        float parameter = ParameterAtDistance(progress);
        int segment = Mathf.Min(Mathf.FloorToInt(parameter), points.Count - 2);
        float t = parameter - segment;
        Vector3 a = points[segment], b = points[segment + 1];
        Vector3 m0 = TangentAt(segment), m1 = TangentAt(segment + 1);
        Vector3 tangent = (6f*t*t - 6f*t)*a + (3f*t*t - 4f*t + 1f)*m0
            + (-6f*t*t + 6f*t)*b + (3f*t*t - 2f*t)*m1;
        if (tangent.sqrMagnitude < 0.000001f) tangent = b - a; // Zero-curvature endpoints.
        return tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.forward;
    }

    /// <summary>
    /// World rotation with local +Z facing decreasing path progress, so models
    /// whose visible front points along local -Z face forward along the path. Uses the
    /// generated path's up direction for roll; progress is clamped like GetPosition.
    /// </summary>
    public Quaternion GetRotation(float progress)
    {
        Vector3 forward = -GetTangent(progress);
        Vector3 up = points.Count == 0
            ? (startingPoint != null ? startingPoint.up : transform.up) : pathUp;
        if (up.sqrMagnitude < 0.000001f) up = Vector3.up;
        // LookRotation needs nonparallel forward/up directions.
        if (Vector3.Cross(up, forward).sqrMagnitude < 0.000001f)
            up = Mathf.Abs(forward.y) < 0.9f ? Vector3.up : Vector3.forward;
        return Quaternion.LookRotation(forward, up);
    }

    /// <summary>Ring in the plane perpendicular to the path. Angle is degrees, radius world units.</summary>
    public Vector3 GetOffsetPosition(float progress, float radius, float angleDegrees)
    {
        Vector3 tangent = GetTangent(progress);
        Vector3 referenceUp = pathUp.sqrMagnitude > 0.000001f ? pathUp.normalized : Vector3.up;
        Vector3 right = Vector3.Cross(referenceUp, tangent);
        if (right.sqrMagnitude < 0.000001f)
            right = Vector3.Cross(Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.forward, tangent);
        right.Normalize();
        Vector3 up = Vector3.Cross(tangent, right).normalized;
        float angle = (Finite(angleDegrees, 0f) % 360f) * Mathf.Deg2Rad;
        return GetPosition(progress) + Mathf.Max(0f, Finite(radius, 0f)) *
            (right * Mathf.Cos(angle) + up * Mathf.Sin(angle));
    }

    private Vector3 TangentAt(int index)
    {
        float strength = Mathf.Clamp01(Finite(curvature, 0.75f));
        if (index == 0) return strength * (points[1] - points[0]);
        if (index == points.Count - 1) return strength * (points[index] - points[index - 1]);
        return strength * 0.5f * (points[index + 1] - points[index - 1]);
    }

    // Cubic Hermite interpolation passes through every waypoint exactly.
    private Vector3 Evaluate(float parameter)
    {
        int segment = Mathf.Min(Mathf.FloorToInt(parameter), points.Count - 2);
        float t = parameter - segment, t2 = t*t, t3 = t2*t;
        return (2*t3 - 3*t2 + 1)*points[segment] + (t3 - 2*t2 + t)*TangentAt(segment)
            + (-2*t3 + 3*t2)*points[segment + 1] + (t3 - t2)*TangentAt(segment + 1);
    }

    private void EnsureSamples()
    {
        if (!samplesDirty) return;
        samplesDirty = false;
        sampleDistances.Clear();
        samplePositions.Clear();
        if (points.Count == 0) return;
        samplePositions.Add(points[0]);
        sampleDistances.Add(0f);
        float length = 0f;
        int subdivisions = Mathf.Clamp(samplesPerSegment, 8, 128);
        int count = (points.Count - 1) * subdivisions;
        for (int i = 1; i <= count; i++)
        {
            Vector3 position = Evaluate((float)i / subdivisions);
            length += Vector3.Distance(samplePositions[samplePositions.Count - 1], position);
            samplePositions.Add(position);
            sampleDistances.Add(length);
        }
    }

    private float ParameterAtDistance(float progress)
    {
        EnsureSamples();
        float target = Mathf.Clamp01(Finite(progress, 0f)) * TotalLength;
        if (target <= 0f || sampleDistances.Count < 2) return 0f;
        int last = sampleDistances.Count - 1;
        if (target >= sampleDistances[last]) return points.Count - 1;
        // Binary search locates two samples bracketing the target distance.
        int low = 0, high = last;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (sampleDistances[middle] < target) low = middle; else high = middle;
        }
        float fraction = Mathf.InverseLerp(sampleDistances[low], sampleDistances[high], target);
        return (low + fraction) / Mathf.Clamp(samplesPerSegment, 8, 128);
    }

    private static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    // Track exact objects rather than deleting every child of the generator:
    // unrelated children (including a custom starting point) are preserved.
    private void ClearGeneratedObjects()
    {
        ClearGeneratedList(generatedObjects);
    }

    private void ClearGeneratedList(List<GameObject> ownedObjects)
    {
        foreach (GameObject item in ownedObjects)
        {
            if (item == null) continue;
            if (Application.isPlaying)
            {
                item.SetActive(false); // Destroy is deferred until the end of the frame.
                Destroy(item);
            }
            else
            {
#if UNITY_EDITOR
                UnityEditor.Undo.DestroyObjectImmediate(item);
#else
                DestroyImmediate(item);
#endif
            }
        }
        ownedObjects.Clear();
    }

    private void OnDrawGizmos()
    {
        EnsureSamples();
        Gizmos.color = pathColor;
        for (int i = 1; i < samplePositions.Count; i++) Gizmos.DrawLine(samplePositions[i - 1], samplePositions[i]);
        float radius = Mathf.Max(0f, Finite(waypointGizmoRadius, 0.6f));
        foreach (Vector3 point in points) Gizmos.DrawWireSphere(point, radius);
    }
}
