using System;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Hardware-free checks. Run Tools/Mission Marshal/Check Component Manager.</summary>
public static class ComponentManagerChecks
{
    [MenuItem("Tools/Mission Marshal/Check Component Manager")]
    public static void Run()
    {
        int checks = 0;
        Action<bool, string> expect = (condition, label) =>
        {
            checks++;
            if (!condition) throw new InvalidOperationException("ComponentManager check: " + label);
        };
        var originalCulture = CultureInfo.CurrentCulture;
        var go = new GameObject("Temporary ComponentManager checks");
        go.SetActive(false); // Prevent port subscriptions or connections during tests.
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var manager = go.AddComponent<ComponentManager>();
            var receive = typeof(ComponentManager).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic);
            Action<bool, string> feed = (first, line) => receive.Invoke(manager, new object[] { first, line });
            for (int row = 1; row <= 4; row++)
                for (int col = 1; col <= 4; col++)
                {
                    expect(PanelSerialProtocol.TryParse($"KEYPAD PRESS row={row} col={col}", out var key), "key parse");
                    expect(key.First == col - 1 && key.Second == 4 - row, "all keypad coordinates");
                }
            string[] invalid = { null, "", "boot ROM text", "ERROR", "FP MATCH id=no confidence=1",
                "FP MATCH id=1", "FP MATCH id=2147483648 confidence=1", "KEYPAD PRESS row=0 col=1",
                "KEYPAD PRESS row=5 col=1", "KEYPAD PRESS row=1 col=-1", "KEYPAD PRESS row=1 col=1 col=2",
                "ULTRASONIC DISTANCE cm=-1", "ULTRASONIC DISTANCE cm=NaN", "ULTRASONIC DISTANCE cm=Infinity",
                "ULTRASONIC DISTANCE cm=1e999", "JOYSTICK POSITION x=-1 y=2000",
                "JOYSTICK POSITION x=4096 y=2000", "JOYSTICK POSITION x=1", "PANEL ACTIVE component=BOGUS" };
            foreach (string line in invalid)
            {
                expect(!PanelSerialProtocol.TryParse(line, out _), "reject malformed/sentinel: " + line);
                feed(true, line);
                feed(false, line); // No exceptions when invalid data reaches either route.
            }
            expect(PanelSerialProtocol.NormalizeAxis(0, 2047.5f) == -1f, "joystick minimum");
            expect(PanelSerialProtocol.NormalizeAxis(4095, 2047.5f) == 1f, "joystick maximum");
            expect(PanelSerialProtocol.NormalizeAxis(3000, 3000) == 0f, "calibrated centre");
            expect(!float.IsNaN(PanelSerialProtocol.NormalizeAxis(2000, float.NaN)), "bad calibration fallback");
            feed(false, "PANEL ACTIVE component=ULTRASONIC");
            feed(false, "ULTRASONIC DISTANCE cm=12.34 echo_us=720");
            expect(Math.Abs(manager.UltrasonicDistanceCm - 12.34f) < 0.001f, "culture-independent distance");
            feed(false, "ULTRASONIC DISTANCE cm=-1");
            expect(Math.Abs(manager.UltrasonicDistanceCm - 12.34f) < 0.001f, "-1 retains previous reading");
            feed(false, "ULTRASONIC DISTANCE cm=999");
            expect(manager.UltrasonicDistanceCm == 125f, "distance cap");
            feed(true, "ULTRASONIC DISTANCE cm=1");
            expect(manager.UltrasonicDistanceCm == 125f, "wrong-board data ignored");
            feed(false, "ULTRASONIC ERROR echo_timeout");
            expect(manager.UltrasonicDistanceCm == 125f && manager.LastUltrasonicJoystickError != null,
                "errors retained without replacing readings");
            feed(false, "PANEL IDLE");
            feed(false, "ULTRASONIC DISTANCE cm=1");
            expect(manager.UltrasonicDistanceCm == 125f && !manager.HasUltrasonicReading, "idle ignores old data");
            feed(false, "PANEL ACTIVE component=JOYSTICK");
            feed(false, "JOYSTICK POSITION x=0 y=4095");
            expect(manager.Joystick == new Vector2(-1, 1), "joystick manager normalization");
            feed(false, "JOYSTICK POSITION x=2200 y=1800");
            expect(manager.Joystick == new Vector2(PanelSerialProtocol.NormalizeAxis(2200, 2047.5f), PanelSerialProtocol.NormalizeAxis(1800, 2047.5f)),
                "component preserves small signed magnitudes without dead zone, swap or inversion");
            feed(true, "PANEL ACTIVE component=KEYPAD");
            int presses = 0;
            manager.KeypadPressed += key => presses++;
            feed(true, "KEYPAD PRESS row=4 col=1");
            feed(true, "KEYPAD PRESS row=4 col=1");
            expect(manager.KeypadPosition == Vector2Int.zero && presses == 2, "repeat presses deliver separate events");
            expect(manager.HasJoystickReading, "boards operate independently");
            feed(true, "KEYPAD PRESS row=1 col=4");
            expect(manager.KeypadPosition == new Vector2Int(3, 3), "bottom-right mapping");
            feed(true, "PANEL IDLE");
            feed(true, "PANEL ACTIVE component=FP");
            feed(true, "FP MATCH id=7 confidence=90");
            expect(manager.HasFingerprintMatch && manager.FingerprintId == 7 && manager.FingerprintConfidence == 90,
                "fingerprint match");
            feed(true, "FP STORED id=8");
            expect(manager.LastStoredFingerprintId == 8, "enrollment result");
            feed(true, "FP NO_MATCH");
            expect(!manager.HasFingerprintMatch && manager.FingerprintId == -1, "no-match clears identity");
            int readResult = -99, readCallbacks = 0;
            manager.FingerprintReadCallback = id => { readResult = id; readCallbacks++; };
            feed(true, "FP MATCH id=12 confidence=70");
            expect(readResult == 12 && readCallbacks == 1, "stored read Action receives ID");
            feed(true, "FP NO_MATCH");
            expect(readResult == -1 && readCallbacks == 2, "stored read Action receives no-match");
            var states = new System.Collections.Generic.List<int>();
            manager.FingerprintEnrollmentCallback = state => states.Add(state);
            typeof(ComponentManager).GetField("enrollingFingerprint", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, true);
            feed(true, "FP ENROLL place_finger_first_scan");
            feed(true, "FP ENROLL first_scan_ok remove_finger");
            feed(true, "FP ENROLL place_same_finger_second_scan");
            feed(true, "FP STORED id=7");
            expect(string.Join(",", states) == "0,1,2,3" && !manager.IsEnrollingFingerprint,
                "enrollment callback gets each stage and success");
            feed(true, "FP STORED id=7");
            expect(states.Count == 4, "completed enrollment does not repeat callback");
            typeof(ComponentManager).GetField("enrollingFingerprint", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, true);
            feed(false, "FP ERROR wrong_board");
            expect(manager.IsEnrollingFingerprint && states.Count == 4, "other board cannot fail enrollment");
            feed(true, "FP ERROR stage=combine_scans reason=scans_do_not_match code=0x0A");
            expect(states[4] == -1 && !manager.IsEnrollingFingerprint, "enrollment failure is -1");
            typeof(ComponentManager).GetField("readingFingerprint", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, true);
            feed(true, "FP ERROR finger_step_timeout");
            expect(readResult == -1 && readCallbacks == 3 && !manager.IsReadingFingerprint, "read error completes callback");
            typeof(ComponentManager).GetField("enrollingFingerprint", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, true);
            typeof(ComponentManager).GetMethod("FirstDisconnected", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
            expect(states.Count == 6 && states[5] == -1, "disconnect fails pending enrollment");
            manager.FingerprintReadCallback = null;
            manager.FingerprintEnrollmentCallback = null;
            feed(true, "PANEL ACTIVE component=FP");
            feed(true, "FP MATCH id=4 confidence=50");
            expect(manager.FingerprintId == 4, "null callback still updates value");
            int rejected = 0;
            manager.ReadFingerprint(id => rejected = id);
            expect(rejected == -1 && !manager.IsReadingFingerprint, "unavailable serial reports failure");
            int firstCallback = 0, replacementCallback = 0;
            feed(true, "PANEL ACTIVE component=KEYPAD");
            manager.SetKeypadCallback(key => firstCallback++);
            manager.SetKeypadCallback(key => replacementCallback++);
            feed(true, "KEYPAD PRESS row=4 col=1");
            expect(firstCallback == 0 && replacementCallback == 1, "setter replaces previous callback");
            manager.DeactivateKeypad();
            feed(true, "PANEL ACTIVE component=KEYPAD");
            feed(true, "KEYPAD PRESS row=4 col=1");
            expect(replacementCallback == 1, "deactivation clears keypad callback");
            int joystickCalls = 0, distanceCalls = 0;
            feed(false, "PANEL ACTIVE component=JOYSTICK");
            manager.SetJoystickCallback(value => joystickCalls++);
            feed(false, "JOYSTICK POSITION x=1000 y=2000");
            feed(false, "PANEL IDLE");
            feed(false, "PANEL ACTIVE component=JOYSTICK");
            feed(false, "JOYSTICK POSITION x=1000 y=2000");
            expect(joystickCalls == 1, "remote release clears joystick callback");
            feed(false, "PANEL ACTIVE component=ULTRASONIC");
            manager.SetUltrasonicCallback(value => distanceCalls++);
            feed(false, "ULTRASONIC DISTANCE cm=25");
            manager.DeactivateUltrasonic();
            feed(false, "PANEL ACTIVE component=ULTRASONIC");
            feed(false, "ULTRASONIC DISTANCE cm=25");
            expect(distanceCalls == 1, "ultrasonic callback invokes then clears on deactivation");
            manager.SetFingerprintReadCallback(id => { });
            manager.SetFingerprintEnrollmentCallback(state => { });
            manager.DeactivateFingerprint();
            expect(manager.FingerprintReadCallback == null && manager.FingerprintEnrollmentCallback == null,
                "fingerprint setters clear even when component already inactive");
            manager.SetKeypadCallback(null);
            manager.SetJoystickCallback(null);
            manager.SetUltrasonicCallback(null);
            Debug.Log($"ComponentManager checks passed: {checks}. No physical serial hardware used.");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
