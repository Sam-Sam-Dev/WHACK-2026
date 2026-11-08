/*
 * CHALLENGE TAKEOVERS
 * GenerateChallengePoints(): ordered progress thresholds, spaced 0.04..0.08.
 * Reaching/passing each point spawns ZapP, then queues a 5-second takeover.
 * A random crew ID (1..PlayerPrefs CrewNumber) must scan its registered finger.
 * FinishedReading(id): correct -> log success; wrong/-1 -> red UI + sound prefab.
 * Reads retry from Update, not recursively inside the serial callback. Timer uses
 * unscaled time and cannot be extended by wrong scans or late sensor responses.
 * Crossed points queue rather than overwrite a takeover already in progress.
 * CrewTakeoverUI owns presentation; ComponentManager owns USB/fingerprint I/O.
 * AssignCrewVolumes(): fixed crew-ID -> universe Volume mapping using saved CrewNumber.
 * Each takeover spawns its own Zap and immediately changes to the requested crew's
 * realm. Only assigned universe volumes switch; baseline post-processing is kept.
 * MovementEnabled freezes ship/progression/orbit until takeover and Zap finish.
 * AssignCrewInputCorruptions() runs once after assigning realms at game start.
 * GetCrewInputCorruption(id), TryGetCrewKeypadSymbol(id,position,out symbol), and
 * Crew keypad mappings expose fixed per-crew rules for tasks. The raw
 * ComponentManager values are unchanged. No minigame behavior is added here.
 */
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public ComponentManager components;
    [Tooltip("Ship movement script providing pastTime and GameLength.")]
    public SpaceShipMove spaceMovie;
    [SerializeField] private List<float> challengePoints = new List<float>();
    public List<float> ChallengePoints => challengePoints;
    private int nextChallengeIndex;
    [Header("Challenge pacing")]
    public Vector2 challengeProgressGap = new Vector2(.04f,.08f);
    [Min(0)] public float challengeResultSeconds = 1.4f;
    [Min(1)] public int startingHearts = 3;
    [Header("Task success feedback")]
    [Min(.01f)] public float successFlashSeconds = .65f;
    [Range(0f,1f)] public float successFlashOpacity = .25f;
    private UnityEngine.UI.Image successFlash;
    private float successFlashEndsAt;
    public GameObject ZapP;
    public ShipSoundEvents soundEvents;
    public bool HasWon { get; private set; }
    public void PlaySound(ShipSoundEvents.Cue cue) { if (soundEvents != null) soundEvents.Play(cue); }
    public MissionTaskController missionTasks;
    public ShipHealthUI healthUI;
    public GameObject failureSparkPrefab;
    [Header("Ship destruction")]
    public GameObject shipExplosionPrefab;
    [Min(0.1f)] public float restartDelaySeconds = 3f;
    [SerializeField] private int hearts = 3;
    public int Hearts => hearts;
    public bool IsGameOver => hearts <= 0;
    private bool taskInProgress;
    private bool takeoverWarned;
    [Header("Crew realms (one volume per universe)")]
    [SerializeField] private List<UnityEngine.Rendering.Volume> universeVolumes = new List<UnityEngine.Rendering.Volume>();
    [SerializeField, HideInInspector] private List<UnityEngine.Rendering.Volume> crewVolumes = new List<UnityEngine.Rendering.Volume>();
    [SerializeField, Min(0.1f)] private float zapLifetime = 3f;
    public IReadOnlyList<UnityEngine.Rendering.Volume> CrewVolumes => crewVolumes;
    public int CrewCount { get; private set; }
    public int ActiveRealmCrew { get; private set; }
    [Header("Generated crew input rules (assigned at game start)")]
    [SerializeField] private List<CrewInputCorruption> crewInputCorruptions = new List<CrewInputCorruption>();
    public IReadOnlyList<CrewInputCorruption> CrewInputCorruptions => crewInputCorruptions;
    private float realmSwitchAt, zapEndsAt;
    private bool realmSwitchPending, ownsMovementPause, movementWasEnabled;

    [Header("Crew takeover")]
    public CrewTakeoverUI takeoverUI;
    public GameObject wrongFingerprintAudioPrefab;
    [SerializeField, Min(0.1f)] private float takeoverSeconds = 8f;
    [SerializeField, Min(0.1f)] private float readRetryInterval = 0.35f;
    private int pendingTakeovers;
    private bool takeoverActive;
    private int requestedCrew;
    private int requestedTask;
    private float takeoverDeadline, nextReadAt, feedbackUntil, nextErrorSoundAt;
    public bool IsTakeoverActive => takeoverActive;
    public int RequestedCrew => requestedCrew;

    public void Start()
    {
        if (spaceMovie == null) spaceMovie = FindFirstObjectByType<SpaceShipMove>();
        if (components == null) components = FindFirstObjectByType<ComponentManager>();
        AssignCrewVolumes();
        AssignCrewInputCorruptions();
        PlaySound(ShipSoundEvents.Cue.MissionStart);
        startingHearts = Mathf.Clamp(startingHearts,1,20);
        hearts = startingHearts;
        if (healthUI != null) healthUI.SetHealth(hearts,startingHearts);
        GenerateChallengePoints();
        if (takeoverUI != null) takeoverUI.Hide();
        if (components != null) StartCoroutine(PrepareFingerprint());
    }

    private IEnumerator PrepareFingerprint()
    {
        while (components != null && !components.IsFingerprintConnectionReady) yield return null;
        if (components != null) components.ActivateFingerprint();
    }

    public void Update()
    {
        UpdateTaskSuccessFlash();
        if (IsGameOver || HasWon) return;
        if (spaceMovie != null && spaceMovie.path != null && spaceMovie.path.WaypointCount > 0 && spaceMovie.GameLength > 0 && spaceMovie.pastTime >= spaceMovie.GameLength && !taskInProgress && !takeoverActive && pendingTakeovers == 0) { WinMission(); return; }
        UpdateRealmTransition();
        ResumeShipWhenReady();
        if (spaceMovie != null && spaceMovie.GameLength > 0 &&
            !float.IsNaN(spaceMovie.GameLength) && !float.IsInfinity(spaceMovie.GameLength) &&
            !float.IsNaN(spaceMovie.pastTime) && !float.IsInfinity(spaceMovie.pastTime))
        {
            float progress = Mathf.Clamp01(spaceMovie.pastTime / spaceMovie.GameLength);
            while (nextChallengeIndex < challengePoints.Count && progress >= challengePoints[nextChallengeIndex])
            {
                nextChallengeIndex++;
                pendingTakeovers++; // Each queued takeover will spawn its own Zap.
            }
        }
        if (!takeoverActive)
        {
            if (Time.unscaledTime < feedbackUntil || Time.unscaledTime < zapEndsAt) return;
            if (taskInProgress) return;
            if (pendingTakeovers > 0) BeginTakeover();
            else if (takeoverUI != null) takeoverUI.Hide();
            return;
        }
        float remaining = Mathf.Max(0, takeoverDeadline - Time.unscaledTime);
        if (remaining > 0 && remaining <= 2 && !takeoverWarned) { takeoverWarned = true; PlaySound(ShipSoundEvents.Cue.TimerWarning); }
        if (takeoverUI != null) takeoverUI.SetTime(remaining, takeoverSeconds);
        if (remaining <= 0) { FinishTakeover(false); return; }
        if (components == null || !components.IsFingerprintConnectionReady) return;
        if (components.FingerprintKeypadActive != PanelSerialProtocol.Component.Fingerprint)
        {
            if (Time.unscaledTime >= nextReadAt)
            {
                components.ActivateFingerprint();
                nextReadAt = Time.unscaledTime + 1f; // Wait for activation acknowledgement.
            }
            return;
        }
        if (!components.IsReadingFingerprint && !components.IsEnrollingFingerprint && Time.unscaledTime >= nextReadAt)
        {
            nextReadAt = Time.unscaledTime + readRetryInterval;
            components.ReadFingerprint(FinishedReading);
        }
    }

    private float SpawnZap(GameObject effect = null)
    {
        var camera = Camera.main;
        if (effect == null) effect = ZapP;
        if (effect == null || camera == null) return 0f;
        PlaySound(ShipSoundEvents.Cue.Zap);
        GameObject zap = Instantiate(effect, camera.transform);
        zap.transform.localPosition = new Vector3(0, 0, 2);
        float duration = Mathf.Max(0.1f, zapLifetime);
        Destroy(zap, duration);
        return duration;
    }

    private void BeginTakeover()
    {
        pendingTakeovers--;
        if (CrewCount < 1) AssignCrewVolumes();
        int crewCount = CrewCount;
        requestedCrew = Random.Range(1, crewCount + 1);
        float progress=spaceMovie!=null && spaceMovie.GameLength>0 ? spaceMovie.pastTime/spaceMovie.GameLength : 0f;
        requestedTask=missionTasks!=null?missionTasks.ChooseTask(progress):0;
        PauseShip();
        float duration = SpawnZap();
        zapEndsAt = Time.unscaledTime + duration;
        realmSwitchAt = Time.unscaledTime;
        realmSwitchPending = true;
        UpdateRealmTransition(); // Switch in the same frame that the Zap spawns.
        takeoverActive = true;
        takeoverWarned = false;
        takeoverDeadline = Time.unscaledTime + Mathf.Max(0.1f, takeoverSeconds);
        nextReadAt = Time.unscaledTime;
        PlaySound(ShipSoundEvents.Cue.FingerprintPrompt);
        if (takeoverUI != null) takeoverUI.Show(requestedCrew, takeoverSeconds, MissionTaskController.GetTaskPreview(requestedTask), requestedTask);
        Debug.Log($"Challenge {nextChallengeIndex}: Crew #{requestedCrew}, take control.", this);
    }

    /// <summary>Crew IDs 1..CrewNumber keep the corresponding volume for the whole game.</summary>
    [ContextMenu("Assign Crew Volumes")]
    public void AssignCrewVolumes()
    {
        CrewCount = Mathf.Clamp(PlayerPrefs.GetInt(FPSetup.CrewNumberPrefKey, 2), 1, 65535);
        crewVolumes.Clear();
        var available = new List<UnityEngine.Rendering.Volume>();
        if (universeVolumes != null)
            foreach (var volume in universeVolumes)
                if (volume != null && !available.Contains(volume)) available.Add(volume);
        for (int i = 0; i < CrewCount; i++)
            crewVolumes.Add(available.Count > 0 ? available[i % available.Count] : null);
        if (available.Count > 0 && CrewCount > available.Count)
            Debug.LogWarning("More crew than realm volumes: extra crew reuse volumes in list order.", this);
    }

    public UnityEngine.Rendering.Volume GetCrewVolume(int crewId) =>
        crewId >= 1 && crewId <= crewVolumes.Count ? crewVolumes[crewId - 1] : null;

    [ContextMenu("Assign Crew Input Corruptions")]
    public void AssignCrewInputCorruptions()
    {
        if (CrewCount < 1) AssignCrewVolumes();
        crewInputCorruptions.Clear();
        var random = new System.Random(System.Guid.NewGuid().GetHashCode());
        var layouts = new HashSet<string>();
        for (int id = 1; id <= CrewCount; id++)
        {
            CrewInputCorruption rules;
            do { rules = CrewInputCorruption.Generate(id, random); }
            while (!layouts.Add(rules.KeypadLayout)); // Different keypad permutation for each crew.
            crewInputCorruptions.Add(rules);
        }
    }

    public CrewInputCorruption GetCrewInputCorruption(int crewId) =>
        crewId >= 1 && crewId <= crewInputCorruptions.Count ? crewInputCorruptions[crewId - 1] : null;

    public bool TryGetCrewKeypadSymbol(int crewId, Vector2Int position, out char symbol)
    {
        symbol = '\0';
        var rules = GetCrewInputCorruption(crewId);
        return rules != null && rules.TryGetKeypadSymbol(position, out symbol);
    }

    private void UpdateRealmTransition()
    {
        if (!realmSwitchPending || Time.unscaledTime < realmSwitchAt) return;
        realmSwitchPending = false;
        var chosen = GetCrewVolume(requestedCrew);
        if (chosen == null) return;
        foreach (var volume in universeVolumes)
        {
            if (volume == null) continue;
            volume.gameObject.SetActive(volume == chosen);
            if (volume == chosen) { volume.enabled = true; volume.isGlobal = true; volume.weight = 1f; }
        }
        ActiveRealmCrew = requestedCrew;
        PlaySound(ShipSoundEvents.Cue.RealmEntered);
        Debug.Log($"Entered Crew #{requestedCrew}'s realm: {chosen.name}", this);
    }

    private void PauseShip()
    {
        if (spaceMovie == null || ownsMovementPause) return;
        movementWasEnabled = spaceMovie.MovementEnabled;
        spaceMovie.MovementEnabled = false;
        ownsMovementPause = true;
    }

    private void ResumeShipWhenReady()
    {
        if (HasWon || IsGameOver || !ownsMovementPause || takeoverActive || taskInProgress || realmSwitchPending || Time.unscaledTime < zapEndsAt) return;
        if (spaceMovie != null) spaceMovie.MovementEnabled = movementWasEnabled;
        ownsMovementPause = false;
    }

    // ComponentManager calls this on Unity's main thread, once each scan returns.
    public void FinishedReading(int result)
    {
        if (!takeoverActive || Time.unscaledTime >= takeoverDeadline) return;
        if (result == requestedCrew) { PlaySound(ShipSoundEvents.Cue.FingerprintAccepted); FinishTakeover(true); return; }
        PlaySound(ShipSoundEvents.Cue.FingerprintRejected);
        if (takeoverUI != null) takeoverUI.ShowWrongFinger();
        PlayWrongFingerprintAudio();
        nextReadAt = Time.unscaledTime + readRetryInterval;
    }

    private void FinishTakeover(bool succeeded)
    {
        takeoverActive = false; // Ignore cancellation callbacks/late results first.
        if (components != null)
        {
            components.SetFingerprintReadCallback(null);
            if (components.IsReadingFingerprint) components.CancelFingerprintOperation();
        }
        feedbackUntil = Time.unscaledTime + 0.8f;
        if (takeoverUI != null) takeoverUI.ShowResult(requestedCrew, succeeded);
        if (succeeded) Debug.Log($"Crew #{requestedCrew} took control successfully.", this);
        else
        {
            Debug.Log($"Crew #{requestedCrew} takeover timed out after {takeoverSeconds:0.#} seconds.", this);
            PlayWrongFingerprintAudio();
            ApplyTaskFailure();
        }
        if (succeeded && missionTasks != null)
        {
            taskInProgress = true;
            if (takeoverUI != null) takeoverUI.Hide();
            missionTasks.StartTask(requestedTask, this, requestedCrew, TaskFinished);
        }
        ResumeShipWhenReady();
    }

    private void TaskFinished(bool success)
    {
        taskInProgress = false;
        if (success) { ShowTaskSuccessFlash(); PlaySound(ShipSoundEvents.Cue.TaskSuccess); RestoreNormalRealm(); }
        else { if (missionTasks != null && missionTasks.display != null) missionTasks.display.Hide(); PlaySound(ShipSoundEvents.Cue.TaskFailure); ApplyTaskFailure(); if(takeoverUI!=null && !IsGameOver)takeoverUI.ShowTaskFailure(hearts,startingHearts); }
        feedbackUntil = Time.unscaledTime + Mathf.Max(0,challengeResultSeconds);
        ResumeShipWhenReady();
    }

    private void ShowTaskSuccessFlash()
    {
        if(successFlash==null)
        {
            var overlay=new GameObject("Task Success Flash",typeof(RectTransform),typeof(Canvas));overlay.transform.SetParent(transform,false);
            var canvas=overlay.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=300;
            var tint=new GameObject("Green Screen Tint",typeof(RectTransform),typeof(UnityEngine.UI.Image));tint.transform.SetParent(overlay.transform,false);
            var rect=tint.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            successFlash=tint.GetComponent<UnityEngine.UI.Image>();successFlash.raycastTarget=false;
        }
        successFlash.gameObject.SetActive(true);
        successFlashEndsAt=Time.unscaledTime+Mathf.Max(.01f,successFlashSeconds);
        UpdateTaskSuccessFlash();
    }

    private void UpdateTaskSuccessFlash()
    {
        if(successFlash==null || !successFlash.gameObject.activeSelf)return;
        float remaining=successFlashEndsAt-Time.unscaledTime;
        successFlash.color=new Color(.15f,1f,.4f,Mathf.Clamp01(remaining/Mathf.Max(.01f,successFlashSeconds))*successFlashOpacity);
        if(remaining<=0f)successFlash.gameObject.SetActive(false);
    }

    private void ApplyTaskFailure()
    {
        if (IsGameOver) return;
        // Spawn the failure spark before removing the crew's realm override.
        float sparkDuration = SpawnZap(ZapP != null ? ZapP : failureSparkPrefab);
        zapEndsAt = Time.unscaledTime + sparkDuration;
        RestoreNormalRealm();
        hearts = Mathf.Max(0, hearts - 1);
        PlaySound(ShipSoundEvents.Cue.HeartLost);
        if (healthUI != null) healthUI.SetHealth(hearts,startingHearts);
        Debug.Log($"Hull strike: {hearts}/{startingHearts} hearts remaining.", this);
        if (IsGameOver)
        {
            PlaySound(ShipSoundEvents.Cue.ShipDestroyed);
            pendingTakeovers = 0;
            if (spaceMovie != null) spaceMovie.MovementEnabled = false;
            if (healthUI != null) healthUI.ShowGameOver();
            if (missionTasks != null) missionTasks.CancelTask();
            if (takeoverUI != null) takeoverUI.Hide();
            if (components != null) components.DeactivateAll();
            ExplodeAndHideShip();
            StartCoroutine(RestartAfterDestruction());
            Debug.Log("Ship destroyed: all hearts lost.", this);
        }
    }

    private void ExplodeAndHideShip()
    {
        if (spaceMovie == null) return;
        Vector3 position = spaceMovie.transform.position;
        float radius = 1f;
        // Keep the ship object and its camera alive while hiding only its visuals.
        foreach (var renderer in spaceMovie.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetComponentInParent<Camera>() != null) continue;
            radius = Mathf.Max(radius, renderer.bounds.extents.magnitude);
            renderer.enabled = false;
        }
        if (shipExplosionPrefab != null)
        {
            var explosion = Instantiate(shipExplosionPrefab, position, Quaternion.identity);
            Destroy(explosion, Mathf.Max(3f, restartDelaySeconds));
            return;
        }
        // A visible default burst works even before an explosion prefab is assigned.
        var burst = new GameObject("Ship Explosion");
        burst.transform.position = position;
        var particles = burst.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 2f, radius * 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(radius * .15f, radius * .45f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f,.25f,.02f), new Color(1f,.9f,.25f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * .3f;
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white,0f), new GradientColorKey(new Color(1f,.1f,0f),1f) },
            new[] { new GradientAlphaKey(1f,0f), new GradientAlphaKey(0f,1f) });
        fade.color = gradient;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var material = new Material(shader);
            burst.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            Destroy(material, 3f);
        }
        particles.Play();
        Destroy(burst, 3f);
    }

    private IEnumerator RestartAfterDestruction()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(.1f, restartDelaySeconds));
        UnityEngine.SceneManagement.SceneManager.LoadScene("Start");
    }

    /// <summary>Remove crew overrides so the scene's baseline volumes take effect again.</summary>
    public void RestoreNormalRealm()
    {
        realmSwitchPending = false;
        if (universeVolumes != null)
            foreach (var volume in universeVolumes)
                if (volume != null) volume.gameObject.SetActive(false);
        PlaySound(ShipSoundEvents.Cue.NormalRealm);
        ActiveRealmCrew = 0; // Zero represents normal space, not a registered crew.
    }

    private void WinMission()
    {
        HasWon = true;
        if (spaceMovie != null) { spaceMovie.pastTime = spaceMovie.GameLength; spaceMovie.transform.position = spaceMovie.path.GetPosition(1); spaceMovie.MovementEnabled = false; }
        if (missionTasks != null) missionTasks.CancelTask();
        if (takeoverUI != null) takeoverUI.Hide();
        if (components != null) components.DeactivateAll();
        RestoreNormalRealm();
        if (healthUI != null) healthUI.ShowVictory();
        PlaySound(ShipSoundEvents.Cue.Victory);
        Debug.Log("Mission complete: destination reached!", this);
    }

    public void PlayTaskErrorAudio() => PlayWrongFingerprintAudio();

    private void PlayWrongFingerprintAudio()
    {
        if (wrongFingerprintAudioPrefab == null || Time.unscaledTime < nextErrorSoundAt) return;
        nextErrorSoundAt = Time.unscaledTime + 0.6f;
        GameObject sound = Instantiate(wrongFingerprintAudioPrefab);
        float lifetime = 0.5f;
        foreach (var source in sound.GetComponentsInChildren<AudioSource>())
        {
            if (source.clip == null) continue;
            source.loop = false;
            source.Play();
            lifetime = Mathf.Max(lifetime, source.clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)) + 0.1f);
        }
        Destroy(sound, lifetime);
    }

    [ContextMenu("Generate Challenge Points")]
    public void GenerateChallengePoints()
    {
        challengePoints.Clear();
        nextChallengeIndex = 0;
        float progress = 0f;
        while (true)
        {
            progress += Random.Range(Mathf.Max(.001f,challengeProgressGap.x), Mathf.Max(Mathf.Max(.001f,challengeProgressGap.x),challengeProgressGap.y));
            if (progress > 0.95f) break;
            challengePoints.Add(progress);
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (missionTasks != null) missionTasks.CancelTask();
        taskInProgress = false;
        takeoverActive = false;
        pendingTakeovers = 0;
        realmSwitchPending = false;
        if (!HasWon && !IsGameOver && ownsMovementPause && spaceMovie != null) spaceMovie.MovementEnabled = movementWasEnabled;
        ownsMovementPause = false;
        if (components != null)
        {
            components.SetFingerprintReadCallback(null);
            if (components.IsReadingFingerprint) components.CancelFingerprintOperation();
        }
        if (takeoverUI != null) takeoverUI.Hide();
    }
}
