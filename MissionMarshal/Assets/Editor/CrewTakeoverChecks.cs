using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class CrewTakeoverChecks
{
    [MenuItem("Tools/Mission Marshal/Check Crew Takeover")]
    public static void Run()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<CrewTakeoverUI>(FindObjectsInactive.Include);
        if (view == null) throw new Exception("Open SampleScene with its takeover overlay first.");
        int checks = 0;
        Action<bool, string> expect = (ok, label) => { checks++; if (!ok) throw new Exception("Takeover: " + label); };
        var go = new GameObject("Temporary takeover checks");
        go.SetActive(false);
        var randomState = UnityEngine.Random.state;
        try
        {
            var manager = go.AddComponent<GameManager>();
            manager.takeoverUI = view;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Action<string, object> set = (name, value) => typeof(GameManager).GetField(name, flags).SetValue(manager, value);
            Func<string, object> get = name => typeof(GameManager).GetField(name, flags).GetValue(manager);
            set("pendingTakeovers", 1);
            typeof(GameManager).GetMethod("BeginTakeover", flags).Invoke(manager, null);
            int crew = manager.RequestedCrew;
            int count = Mathf.Clamp(PlayerPrefs.GetInt(FPSetup.CrewNumberPrefKey, 2), 1, 65535);
            expect(crew >= 1 && crew <= count && manager.IsTakeoverActive, "crew selection uses saved count");
            expect(Mathf.Abs((float)get("takeoverDeadline") - Time.unscaledTime - 8) < .05f, "eight-second demo takeover window");
            expect(view.gameObject.activeSelf && view.crewPrompt.text.Contains("TAKE CONTROL"), "top prompt shown");
            expect(view.timerSlider.value == 1, "timer starts full");
            var deadline = get("takeoverDeadline");
            manager.FinishedReading(crew == 1 ? 2 : 1);
            expect(manager.IsTakeoverActive && view.feedback.text.Contains("WRONG FINGER") && view.wrongScreenFlash.color.a > 0,
                "wrong crew stays active with red feedback");
            expect(Equals(deadline, get("takeoverDeadline")), "wrong finger cannot extend timer");
            view.SetTime(4.5f, 5); Color plenty = view.timerFill.color;
            view.SetTime(.5f, 5); Color urgent = view.timerFill.color;
            expect(plenty.b > urgent.b && urgent.r > plenty.r && view.timerSlider.value == .1f, "slider drains and shifts toward red");
            manager.FinishedReading(crew);
            expect(!manager.IsTakeoverActive && view.crewPrompt.text.Contains("CONTROL GRANTED"), "matching finger completes takeover");
            string success = view.crewPrompt.text;
            manager.FinishedReading(-1);
            expect(view.crewPrompt.text == success, "late results ignored");
            set("pendingTakeovers", 1);
            typeof(GameManager).GetMethod("BeginTakeover", flags).Invoke(manager, null);
            set("takeoverDeadline", Time.unscaledTime - 1);
            manager.Update();
            expect(!manager.IsTakeoverActive && view.timerSlider.value == 0 && view.crewPrompt.text.Contains("EXPIRED"), "timeout ends challenge");
            var ship = go.AddComponent<SpaceShipMove>();
            manager.spaceMovie = ship;
            ship.GameLength = 1; ship.pastTime = 1;
            manager.ChallengePoints.AddRange(new[] { .1f, .2f, .3f });
            manager.Update();
            expect((int)get("nextChallengeIndex") == 3 && (int)get("pendingTakeovers") == 3, "time jump queues every crossed challenge");
            manager.Update();
            expect((int)get("pendingTakeovers") == 3, "crossed challenges do not repeat");
            typeof(GameManager).GetMethod("OnDisable", flags).Invoke(manager, null);
            expect(!view.gameObject.activeSelf && (int)get("pendingTakeovers") == 0, "disable clears queued challenges/UI");
            Debug.Log($"Crew takeover checks passed: {checks}. No hardware or sound playback used.");
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            view.Show(2, 5); view.Hide();
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
