/*
 * START-SCENE CREW ENROLLMENT
 * CrewNumber controls how many fingerprints are saved, using IDs 1..CrewNumber.
 * SetupFingers() runs automatically on Start; it waits without blocking Unity.
 * ComponentManager enrollment callback: 0=first scan, 1=remove, 2=second scan,
 * 3=saved, -1=failed. A failure retries the SAME crew ID after showing a prompt.
 * Assign CrewText (prompt), TitleText and HideOnMissionStart (e.g. sensor image).
 * On completion, hide those extras, show Starting Mission, save PlayerPrefs
 * "CrewNumber", and load MissionSceneName (default SampleScene).
 */
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FPSetup : MonoBehaviour
{
    public const string CrewNumberPrefKey = "CrewNumber";
    public ComponentManager components;
    [Min(1)] public int CrewNumber = 2;
    public TMP_Text CrewText;
    public TMP_Text TitleText;
    public GameObject[] HideOnMissionStart;
    public string MissionSceneName = "SampleScene";
    [SerializeField, Min(0f)] private float missionStartDelay = 1f;

    private int enstate = -2; // -2 means waiting for an enrollment message.
    private int currentCrew;
    private bool settingUp, waitingForEnrollment, ownsFingerprint;
    private bool activationInProgress;

    private void Start() { StartCoroutine(SetupFingers()); }

    // Only the enrollment callback changes scan prompts. No guessed scan delays.
    public void EnrollState(int state)
    {
        if (!waitingForEnrollment) return;
        enstate = state;
        switch (state)
        {
            case 0: Prompt($"Crew #{currentCrew}: place your finger on the sensor."); break;
            case 1: Prompt($"Crew #{currentCrew}: first scan complete. Remove your finger."); break;
            case 2: Prompt($"Crew #{currentCrew}: place the SAME finger on the sensor again."); break;
            case 3: Prompt($"Crew #{currentCrew} registered! Remove your finger."); break;
            case -1: Prompt($"Crew #{currentCrew}: enrollment failed. Remove your finger; we will retry."); break;
        }
    }

    private void Prompt(string text)
    {
        if (CrewText == null) return;
        string crewLabel = "Crew #" + currentCrew;
        CrewText.text = currentCrew > 0 ? text.Replace(crewLabel, CrewIdentity.Colorize(currentCrew, crewLabel)) : text;
    }

    public IEnumerator SetupFingers()
    {
        if (settingUp) yield break;
        settingUp = true;
        if (components == null) components = FindFirstObjectByType<ComponentManager>();
        if (components == null || CrewText == null)
        {
            Prompt("Setup unavailable: assign the component manager and crew prompt.");
            Debug.LogWarning("FPSetup needs ComponentManager and CrewText references.", this);
            settingUp = false;
            yield break;
        }
        if (CrewNumber < 1 || CrewNumber > 65535)
        {
            Prompt("Set a valid crew number before starting enrollment.");
            settingUp = false;
            yield break;
        }
        if (string.IsNullOrWhiteSpace(MissionSceneName) || !Application.CanStreamedLevelBeLoaded(MissionSceneName))
        {
            Prompt("Mission scene is unavailable. Add it to Build Settings.");
            settingUp = false;
            yield break;
        }
        int crewCount = CrewNumber; // Keep this setup consistent if the Inspector changes later.
        if (TitleText != null) TitleText.text = "Crew Registration";
        for (currentCrew = 1; currentCrew <= crewCount; currentCrew++)
        {
            bool saved = false;
            while (!saved)
            {
                // Reconnect/reactivate as needed after a disconnect or sensor error.
                yield return EnsureFingerprintActive();
                if (components == null || !components.isActiveAndEnabled)
                {
                    Prompt("Control panel unavailable. Restart crew setup after reconnecting.");
                    settingUp = false;
                    yield break;
                }
                enstate = -2;
                waitingForEnrollment = true;
                Prompt($"Crew #{currentCrew} of {crewCount}: place your finger on the sensor.");
                components.EnrollFingerprint(currentCrew, EnrollState);
                // ESP steps time out after 15s; a longer overall guard also covers
                // a missing serial response. Every wait yields frames to Unity.
                double deadline = Time.realtimeSinceStartupAsDouble + 50;
                while (enstate != 3 && enstate != -1)
                {
                    if (components == null || !components.IsFingerprintConnectionReady ||
                        Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        EnrollState(-1);
                        if (components != null) components.CancelFingerprintOperation();
                        break;
                    }
                    yield return null;
                }
                saved = enstate == 3;
                waitingForEnrollment = false;
                components.SetFingerprintEnrollmentCallback(null);
                if (!saved)
                {
                    components.CancelFingerprintOperation();
                    components.DeactivateFingerprint();
                    yield return new WaitForSecondsRealtime(2f);
                }
                else
                {
                    // Let this crew member clear the sensor before the next person.
                    yield return new WaitForSecondsRealtime(1.5f);
                }
            }
        }
        components.DeactivateFingerprint();
        ownsFingerprint = false;
        ShowMissionStarting();
        // Give the final message time to render before switching scenes.
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, missionStartDelay));
        PlayerPrefs.SetInt(CrewNumberPrefKey, crewCount);
        PlayerPrefs.Save();
        yield return SceneManager.LoadSceneAsync(MissionSceneName);
    }

    private void ShowMissionStarting()
    {
        if (TitleText != null && TitleText != CrewText) TitleText.gameObject.SetActive(false);
        if (HideOnMissionStart != null)
            foreach (GameObject item in HideOnMissionStart)
                if (item != null && item != CrewText.gameObject && !CrewText.transform.IsChildOf(item.transform) &&
                    !transform.IsChildOf(item.transform)) item.SetActive(false);
        CrewText.gameObject.SetActive(true);
        Prompt("Starting Mission");
        if (CrewText.GetComponentInParent<Canvas>() != null)
        {
            CrewText.rectTransform.anchorMin = CrewText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            CrewText.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    private IEnumerator EnsureFingerprintActive()
    {
        while (components != null && components.isActiveAndEnabled)
        {
            if (!components.IsFingerprintConnectionReady)
            {
                Prompt("Connecting to the fingerprint control panel...");
                yield return null;
                continue;
            }
            if (!activationInProgress)
            {
                Prompt("Initialising fingerprint sensor...");
                components.ActivateFingerprint();
                ownsFingerprint = true;
                activationInProgress = true;
            }
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (components.FingerprintKeypadActive != PanelSerialProtocol.Component.Fingerprint &&
                   components.IsFingerprintConnectionReady && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            activationInProgress = false;
            if (components.FingerprintKeypadActive == PanelSerialProtocol.Component.Fingerprint) yield break;
            Prompt("Fingerprint sensor not ready. Check the connection; retrying...");
            yield return new WaitForSecondsRealtime(2f);
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        waitingForEnrollment = settingUp = activationInProgress = false;
        if (components != null && ownsFingerprint)
        {
            components.SetFingerprintEnrollmentCallback(null);
            components.DeactivateFingerprint();
        }
        ownsFingerprint = false;
    }
}
