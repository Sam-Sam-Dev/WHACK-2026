using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CrewRealmChecks
{
    [MenuItem("Tools/Mission Marshal/Check Crew Realms")]
    public static void Run()
    {
        bool hadPreference = PlayerPrefs.HasKey(FPSetup.CrewNumberPrefKey);
        int oldCount = PlayerPrefs.GetInt(FPSetup.CrewNumberPrefKey);
        var randomState = UnityEngine.Random.state;
        var root = new GameObject("Temporary crew realm checks"); root.SetActive(false);
        int checks = 0;
        Action<bool, string> expect = (ok, label) => { checks++; if (!ok) throw new Exception("Realm check: " + label); };
        try
        {
            var manager = root.AddComponent<GameManager>();
            var path = root.AddComponent<RandomCurvedPath>(); path.GeneratePath(2);
            var ship = root.AddComponent<SpaceShipMove>(); ship.path = path; ship.pastTime = 5; ship.GameLength = 100;
            manager.spaceMovie = ship;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Action<string, object> set = (name, value) => typeof(GameManager).GetField(name, flags).SetValue(manager, value);
            Action<string> call = name => typeof(GameManager).GetMethod(name, flags).Invoke(manager, null);
            var volumes = new List<Volume>();
            for (int i = 0; i < 3; i++) { var go = new GameObject("Test universe " + i); go.transform.SetParent(root.transform); volumes.Add(go.AddComponent<Volume>()); }
            set("universeVolumes", volumes);
            PlayerPrefs.SetInt(FPSetup.CrewNumberPrefKey, 3);
            manager.AssignCrewVolumes();
            expect(manager.CrewCount == 3 && manager.CrewVolumes.Count == 3, "saved integer determines mapping count");
            for (int i = 1; i <= 3; i++) expect(manager.GetCrewVolume(i) == volumes[i - 1], "stable crew-to-volume assignment");
            expect(manager.GetCrewVolume(0) == null && manager.GetCrewVolume(4) == null, "invalid crew ID safe");
            set("pendingTakeovers", 1); call("BeginTakeover");
            expect(!ship.MovementEnabled, "takeover pauses ship");
            var position = ship.transform.position; float elapsed = ship.pastTime;
            typeof(SpaceShipMove).GetMethod("Update", flags).Invoke(ship, null);
            expect(ship.pastTime == elapsed && ship.transform.position == position, "disabled movement freezes path time and position");
            int previousCrew = manager.ActiveRealmCrew;
            int nextCrew = previousCrew % 3 + 1;
            set("requestedCrew", nextCrew); set("realmSwitchPending", true); set("realmSwitchAt", Time.unscaledTime + 100);
            call("UpdateRealmTransition");
            expect(manager.ActiveRealmCrew == previousCrew, "realm does not switch before scheduled midpoint");
            set("realmSwitchAt", Time.unscaledTime - 1); call("UpdateRealmTransition");
            expect(manager.ActiveRealmCrew == nextCrew, "realm switches when scheduled midpoint is reached/passed");
            for (int i = 0; i < 3; i++) expect(volumes[i].gameObject.activeSelf == (i + 1 == nextCrew), "only chosen universe active");
            set("zapEndsAt", Time.unscaledTime + 100);
            manager.FinishedReading(nextCrew);
            expect(!manager.IsTakeoverActive && !ship.MovementEnabled, "early authentication keeps movement paused through remaining Zap");
            set("zapEndsAt", Time.unscaledTime - 1); manager.Update();
            expect(ship.MovementEnabled, "movement resumes once takeover and Zap are finished");
            ship.MovementEnabled = false;
            set("pendingTakeovers", 1); call("BeginTakeover");
            set("takeoverDeadline", Time.unscaledTime - 1); manager.Update();
            expect(!ship.MovementEnabled && !manager.IsTakeoverActive, "pre-existing pause survives timeout");
            PlayerPrefs.SetInt(FPSetup.CrewNumberPrefKey, 0); manager.AssignCrewVolumes();
            expect(manager.CrewCount == 1, "invalid saved crew count clamped");
            Debug.Log($"Crew realm checks passed: {checks}. No hardware, audio or persistent preference changes.");
        }
        finally
        {
            if (hadPreference) PlayerPrefs.SetInt(FPSetup.CrewNumberPrefKey, oldCount);
            else PlayerPrefs.DeleteKey(FPSetup.CrewNumberPrefKey);
            UnityEngine.Random.state = randomState;
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
