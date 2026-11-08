/*
 * UNITY API / ESP32 COMMANDS
 * ActivateFingerprint() -> RELEASE + ACTIVATE FP on fingerprint/keypad ESP
 * ReadFingerprint() / EnrollFingerprint(id) -> FP READ / FP ENROLL <id>
 * ActivateKeypad()      -> RELEASE + ACTIVATE KEYPAD on fingerprint/keypad ESP
 * ActivateUltrasonic()  -> RELEASE + ACTIVATE ULTRASONIC on ultrasonic/joystick ESP
 * ActivateJoystick()    -> RELEASE + ACTIVATE JOYSTICK on ultrasonic/joystick ESP
 * DeactivateFingerprint/Keypad/Ultrasonic/Joystick() -> RELEASE on its own board
 * DeactivateAll() -> RELEASE on both boards. One component PER BOARD may be active.
 * SetFingerprintReadCallback / SetFingerprintEnrollmentCallback -> Action<int>
 * SetKeypadCallback -> Action<Vector2Int>; SetJoystickCallback -> Action<Vector2>
 * SetUltrasonicCallback -> Action<float> (cm). Setters replace the previous callback.
 * Set callbacks after requesting activation. Deactivation, switching away and
 * disconnect clear that component's callbacks; re-register them for the next use.
 *
 * SETUP: assign two DIFFERENT Esp32SerialConnection components in the Inspector;
 * set their USB port names independently. Wait until both connections finish booting.
 * ActivateFingerprint() first, then wait for FingerprintKeypadActive == Fingerprint.
 * ReadFingerprint(Action<int>) stores a callback: matched ID, or -1 on failure.
 * EnrollFingerprint(id, Action<int>) stores a callback: 0=first scan, 1=remove,
 * 2=second scan, 3=saved, -1=failed/cancelled. Null callbacks are safe. No waiting
 * loops, coroutine handles or async framework: serial messages invoke the Actions.
 * Keypad, joystick
 * and ultrasonic start streaming automatically after activation.
 *
 * DATA: properties expose the latest valid readings; Has... flags indicate validity
 * in the current activation. C# events announce each reading, including repeated
 * presses of the same key. Keypad uses (4-row,col-1); distances reject -1 and cap
 * at 125 cm; joystick is independently normalized to -1..1 on each axis.
 * Inspector centre values allow adjustment to a real joystick's rest position.
 *
 * ROUTING: subscribe to main-thread serial line events; acknowledge selection via
 * PANEL ACTIVE/IDLE, then apply valid data only from the correct board/component.
 * Bad lines/diagnostics are ignored. ESP errors update Last...Error without throwing
 * or overwriting valid measurements. Transport loss clears that board's selection.
 * Parsing/calibration lives in PanelSerialProtocol.cs, USB I/O in Esp32SerialConnection.
 */
using System;
using UnityEngine;
using Component = PanelSerialProtocol.Component;
using Kind = PanelSerialProtocol.Kind;

public sealed class ComponentManager : MonoBehaviour
{
    [Header("Two physical ESP32 connections")]
    [SerializeField] private Esp32SerialConnection fingerprintKeypadEsp;
    [SerializeField] private Esp32SerialConnection ultrasonicJoystickEsp;
    [Header("Joystick raw centre calibration (12-bit ADC)")]
    [SerializeField, Range(1f, 4094f)] private float joystickCentreX = 2047.5f;
    [SerializeField, Range(1f, 4094f)] private float joystickCentreY = 2047.5f;
    [Header("Latest values (updated at runtime)")]
    [SerializeField] private Component fingerprintKeypadActive;
    [SerializeField] private Component ultrasonicJoystickActive;
    [SerializeField] private float ultrasonicDistanceCm;
    [SerializeField] private Vector2 joystick;
    [SerializeField] private Vector2Int keypadPosition = new Vector2Int(-1, -1);
    [SerializeField] private int fingerprintId = -1;
    [SerializeField] private int fingerprintConfidence;
    [SerializeField] private int lastStoredFingerprintId = -1;

    // Stored callbacks: read callback remains assigned for subsequent read results.
    // Enrollment progress is delivered only while an enrollment is pending.
    public Action<int> FingerprintReadCallback;
    public Action<int> FingerprintEnrollmentCallback;
    private bool readingFingerprint, enrollingFingerprint;
    public bool IsReadingFingerprint => readingFingerprint;
    public bool IsEnrollingFingerprint => enrollingFingerprint;
    public bool IsUltrasonicJoystickConnectionReady => secondBound != null && secondBound.IsConnected;
    public bool IsFingerprintConnectionReady => firstBound != null && firstBound.IsConnected;

    private Esp32SerialConnection firstBound, secondBound;
    public Component FingerprintKeypadActive => fingerprintKeypadActive;
    public Component UltrasonicJoystickActive => ultrasonicJoystickActive;
    public float UltrasonicDistanceCm => ultrasonicDistanceCm;
    public Vector2 Joystick => joystick;
    public Vector2Int RawJoystick { get; private set; }
    public Vector2Int KeypadPosition => keypadPosition;
    public int FingerprintId => fingerprintId;
    public int FingerprintConfidence => fingerprintConfidence;
    public int LastStoredFingerprintId => lastStoredFingerprintId;
    public bool HasUltrasonicReading { get; private set; }
    public bool HasJoystickReading { get; private set; }
    public bool HasKeypadPress { get; private set; }
    public bool HasFingerprintMatch { get; private set; }
    public string LastFingerprintKeypadError { get; private set; }
    public string LastUltrasonicJoystickError { get; private set; }
    public event Action<float> UltrasonicUpdated;
    public event Action<Vector2> JoystickUpdated;
    public event Action<Vector2Int> KeypadPressed;
    public event Action<int, int> FingerprintMatched;
    public event Action FingerprintNotMatched;
    public event Action<int> FingerprintStored;

    // Replace rather than accumulate listeners. Passing null explicitly clears one.
    public void SetFingerprintReadCallback(Action<int> callback) => FingerprintReadCallback = callback;
    public void SetFingerprintEnrollmentCallback(Action<int> callback) => FingerprintEnrollmentCallback = callback;
    public void SetKeypadCallback(Action<Vector2Int> callback) => KeypadPressed = callback;
    public void SetUltrasonicCallback(Action<float> callback) => UltrasonicUpdated = callback;
    public void SetJoystickCallback(Action<Vector2> callback) => JoystickUpdated = callback;

    private void ClearCallbacks(Component component)
    {
        switch (component)
        {
            case Component.Fingerprint:
                FingerprintReadCallback = null;
                FingerprintEnrollmentCallback = null;
                FingerprintMatched = null;
                FingerprintNotMatched = null;
                FingerprintStored = null;
                break;
            case Component.Keypad: KeypadPressed = null; break;
            case Component.Ultrasonic: UltrasonicUpdated = null; break;
            case Component.Joystick: JoystickUpdated = null; break;
        }
    }

    private void OnEnable()
    {
        if (fingerprintKeypadEsp != null && fingerprintKeypadEsp == ultrasonicJoystickEsp)
        {
            Debug.LogWarning("ComponentManager needs two different ESP32 connections.", this);
            return;
        }
        firstBound = fingerprintKeypadEsp;
        secondBound = ultrasonicJoystickEsp;
        if (firstBound != null)
        {
            firstBound.LineReceived += ReceiveFirst;
            firstBound.Disconnected += FirstDisconnected;
            if (firstBound.IsConnected) firstBound.TrySendCommand("STATUS");
        }
        if (secondBound != null)
        {
            secondBound.LineReceived += ReceiveSecond;
            secondBound.Disconnected += SecondDisconnected;
            if (secondBound.IsConnected) secondBound.TrySendCommand("STATUS");
        }
    }

    private void OnDisable()
    {
        if (firstBound != null)
        {
            firstBound.LineReceived -= ReceiveFirst;
            firstBound.Disconnected -= FirstDisconnected;
            if (firstBound.IsConnected) firstBound.TrySendCommand("RELEASE");
        }
        if (secondBound != null)
        {
            secondBound.LineReceived -= ReceiveSecond;
            secondBound.Disconnected -= SecondDisconnected;
            if (secondBound.IsConnected) secondBound.TrySendCommand("RELEASE");
        }
        FirstDisconnected();
        SecondDisconnected();
        firstBound = secondBound = null;
    }

    [ContextMenu("Activate Fingerprint")]
    public void ActivateFingerprint() => Activate(true, "FP");
    [ContextMenu("Activate Keypad")]
    public void ActivateKeypad() => Activate(true, "KEYPAD");
    [ContextMenu("Activate Ultrasonic")]
    public void ActivateUltrasonic() => Activate(false, "ULTRASONIC");
    [ContextMenu("Activate Joystick")]
    public void ActivateJoystick() => Activate(false, "JOYSTICK");

    [ContextMenu("Deactivate Fingerprint")]
    public void DeactivateFingerprint() => DeactivateIfSelected(true, Component.Fingerprint);
    [ContextMenu("Deactivate Keypad")]
    public void DeactivateKeypad() => DeactivateIfSelected(true, Component.Keypad);
    [ContextMenu("Deactivate Ultrasonic")]
    public void DeactivateUltrasonic() => DeactivateIfSelected(false, Component.Ultrasonic);
    [ContextMenu("Deactivate Joystick")]
    public void DeactivateJoystick() => DeactivateIfSelected(false, Component.Joystick);

    [ContextMenu("Deactivate All")]
    public void DeactivateAll()
    {
        Release(true); Release(false);
        ClearCallbacks(Component.Fingerprint); ClearCallbacks(Component.Keypad);
        ClearCallbacks(Component.Ultrasonic); ClearCallbacks(Component.Joystick);
    }

    [ContextMenu("Read Fingerprint")]
    public void ReadFingerprint() => ReadFingerprint(FingerprintReadCallback);

    public void ReadFingerprint(Action<int> callback)
    {
        if (readingFingerprint || enrollingFingerprint)
        {
            SetError(true, "Fingerprint sensor is busy");
            InvokeFingerprintCallback(callback, -1);
            return;
        }
        FingerprintReadCallback = callback;
        HasFingerprintMatch = false;
        fingerprintId = -1;
        fingerprintConfidence = 0;
        if (fingerprintKeypadActive != Component.Fingerprint)
        {
            SetError(true, "Activate fingerprint mode before reading");
            InvokeFingerprintCallback(callback, -1);
            return;
        }
        readingFingerprint = true;
        if (!Send(true, "FP READ")) FailFingerprintCallbacks();
    }

    public void EnrollFingerprint(int id) => EnrollFingerprint(id, FingerprintEnrollmentCallback);

    public void EnrollFingerprint(int id, Action<int> callback)
    {
        if (readingFingerprint || enrollingFingerprint)
        {
            SetError(true, "Fingerprint sensor is busy");
            InvokeFingerprintCallback(callback, -1);
            return;
        }
        FingerprintEnrollmentCallback = callback;
        if (fingerprintKeypadActive != Component.Fingerprint || id < 1 || id > 65535)
        {
            SetError(true, "Enrollment needs active fingerprint mode and a positive valid ID");
            InvokeFingerprintCallback(callback, -1);
            return;
        }
        lastStoredFingerprintId = -1;
        enrollingFingerprint = true;
        if (!Send(true, "FP ENROLL " + id)) FailFingerprintCallbacks();
    }

    [ContextMenu("Cancel Fingerprint Operation")]
    public void CancelFingerprintOperation()
    {
        if (fingerprintKeypadActive == Component.Fingerprint) Send(true, "FP CANCEL");
        FailFingerprintCallbacks();
    }

    // Snapshot both callbacks and clear pending flags BEFORE invoking game code.
    // Null Actions and exceptions in a game callback cannot break serial handling.
    private void FailFingerprintCallbacks()
    {
        var read = readingFingerprint ? FingerprintReadCallback : null;
        var enroll = enrollingFingerprint ? FingerprintEnrollmentCallback : null;
        readingFingerprint = enrollingFingerprint = false;
        InvokeFingerprintCallback(read, -1);
        InvokeFingerprintCallback(enroll, -1);
    }

    private static void InvokeFingerprintCallback(Action<int> callback, int value)
    {
        if (callback == null) return;
        foreach (Action<int> listener in callback.GetInvocationList())
            try { listener(value); } catch (Exception exception) { Debug.LogException(exception); }
    }

    private Component firstRequested, secondRequested;
    private void Activate(bool first, string name)
    {
        if (!PanelSerialProtocol.TryComponent(name, out Component requested)) return;
        if (first) FailFingerprintCallbacks();
        if (!Send(first, "RELEASE")) return;
        ClearCallbacks(first ? fingerprintKeypadActive : ultrasonicJoystickActive);
        ClearCallbacks(first ? firstRequested : secondRequested);
        ClearBoard(first);
        if (first) firstRequested = requested; else secondRequested = requested;
        Send(first, "ACTIVATE " + name);
    }

    private void DeactivateIfSelected(bool first, Component component)
    {
        // Include pending activation so immediate Activate/Deactivate calls also work.
        if ((first ? fingerprintKeypadActive : ultrasonicJoystickActive) == component ||
            (first ? firstRequested : secondRequested) == component ||
            (first && component == Component.Fingerprint && (readingFingerprint || enrollingFingerprint))) Release(first);
        ClearCallbacks(component); // Explicit deactivation also clears preassigned inactive callbacks.
    }

    private void Release(bool first)
    {
        if (first) FailFingerprintCallbacks();
        Send(first, "RELEASE");
        ClearCallbacks(first ? fingerprintKeypadActive : ultrasonicJoystickActive);
        ClearCallbacks(first ? firstRequested : secondRequested);
        ClearBoard(first);
        if (first) firstRequested = Component.None; else secondRequested = Component.None;
    }

    private bool Send(bool first, string command)
    {
        var connection = first ? firstBound : secondBound;
        if (!isActiveAndEnabled || connection == null || !connection.IsConnected)
        { SetError(first, "ESP32 is unassigned, disconnected, or still booting."); return false; }
        if (connection.TrySendCommand(command)) return true;
        SetError(first, "Could not send ESP32 command: " + command);
        return false;
    }

    private void SetError(bool first, string error)
    {
        if (first) LastFingerprintKeypadError = error;
        else LastUltrasonicJoystickError = error;
    }

    private void ClearBoard(bool first)
    {
        if (first)
        {
            fingerprintKeypadActive = Component.None;
            HasKeypadPress = HasFingerprintMatch = false;
        }
        else
        {
            ultrasonicJoystickActive = Component.None;
            HasJoystickReading = HasUltrasonicReading = false;
        }
    }
    private void FirstDisconnected()
    {
        FailFingerprintCallbacks();
        ClearCallbacks(Component.Fingerprint);
        ClearCallbacks(Component.Keypad);
        ClearBoard(true); firstRequested = Component.None;
    }
    private void SecondDisconnected()
    {
        ClearCallbacks(Component.Ultrasonic); ClearCallbacks(Component.Joystick);
        ClearBoard(false); secondRequested = Component.None;
    }
    private void ReceiveFirst(string line) => Receive(true, line);
    private void ReceiveSecond(string line) => Receive(false, line);

    private void Receive(bool first, string line)
    {
        if (!PanelSerialProtocol.TryParse(line, out var message)) return;
        if (message.Type == Kind.Error)
        {
            SetError(first, line);
            if (first && (message.Component == Component.Fingerprint || line.StartsWith("PANEL ERROR", StringComparison.Ordinal)))
                FailFingerprintCallbacks();
            return;
        }
        bool correctBoard = message.Component == Component.None ||
            (first ? message.Component == Component.Fingerprint || message.Component == Component.Keypad
                   : message.Component == Component.Ultrasonic || message.Component == Component.Joystick);
        if (!correctBoard) return;
        if (message.Type == Kind.Selection)
        {
            Component previous = first ? fingerprintKeypadActive : ultrasonicJoystickActive;
            if (previous != message.Component)
            {
                if (first && previous == Component.Fingerprint) FailFingerprintCallbacks();
                ClearCallbacks(previous);
            }
            if (first) fingerprintKeypadActive = message.Component;
            else ultrasonicJoystickActive = message.Component;
            if (message.Component != Component.None)
            {
                if (first) firstRequested = Component.None;
                else secondRequested = Component.None;
            }
            if (message.Component == Component.None) ClearBoard(first);
            if (first && message.Component != Component.Fingerprint) FailFingerprintCallbacks();
            return;
        }
        if ((first ? fingerprintKeypadActive : ultrasonicJoystickActive) != message.Component) return;
        switch (message.Type)
        {
            case Kind.Distance:
                ultrasonicDistanceCm = message.Value;
                HasUltrasonicReading = true;
                UltrasonicUpdated?.Invoke(ultrasonicDistanceCm);
                break;
            case Kind.Joystick:
                RawJoystick = new Vector2Int(message.First, message.Second);
                joystick = new Vector2(PanelSerialProtocol.NormalizeAxis(message.First, joystickCentreX),
                    PanelSerialProtocol.NormalizeAxis(message.Second, joystickCentreY));
                HasJoystickReading = true;
                JoystickUpdated?.Invoke(joystick);
                break;
            case Kind.KeypadPress:
                keypadPosition = new Vector2Int(message.First, message.Second);
                Debug.Log($"KEYPAD converted: row={keypadPosition.y}, column={keypadPosition.x} (x={keypadPosition.x}, y={keypadPosition.y}; top-left=0,0)", this);
                HasKeypadPress = true;
                KeypadPressed?.Invoke(keypadPosition);
                break;
            case Kind.FingerprintMatch:
                fingerprintId = message.First;
                fingerprintConfidence = message.Second;
                HasFingerprintMatch = true;
                readingFingerprint = false;
                InvokeFingerprintCallback(FingerprintReadCallback, fingerprintId);
                FingerprintMatched?.Invoke(fingerprintId, fingerprintConfidence);
                break;
            case Kind.FingerprintNoMatch:
                fingerprintId = -1;
                fingerprintConfidence = 0;
                HasFingerprintMatch = false;
                readingFingerprint = false;
                InvokeFingerprintCallback(FingerprintReadCallback, -1);
                FingerprintNotMatched?.Invoke();
                break;
            case Kind.FingerprintStored:
                lastStoredFingerprintId = message.First;
                bool wasEnrolling = enrollingFingerprint;
                enrollingFingerprint = false;
                if (wasEnrolling) InvokeFingerprintCallback(FingerprintEnrollmentCallback, 3);
                FingerprintStored?.Invoke(lastStoredFingerprintId);
                break;
            case Kind.FingerprintProgress:
                if (enrollingFingerprint)
                {
                    if (message.Text == "place_finger_first_scan") InvokeFingerprintCallback(FingerprintEnrollmentCallback, 0);
                    else if (message.Text == "first_scan_ok") InvokeFingerprintCallback(FingerprintEnrollmentCallback, 1);
                    else if (message.Text == "place_same_finger_second_scan") InvokeFingerprintCallback(FingerprintEnrollmentCallback, 2);
                }
                break;
            case Kind.FingerprintCancelled:
                FailFingerprintCallbacks();
                break;
        }
    }
}
