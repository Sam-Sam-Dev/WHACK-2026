# Component Manager

Add two `Esp32SerialConnection` components (with different USB port names), then
add `ComponentManager` and assign its two Inspector connection references:

- **Fingerprint Keypad Esp**: board running fingerprint + keypad firmware.
- **Ultrasonic Joystick Esp**: board running ultrasonic + joystick firmware.

Wait for serial connections to finish their two-second boot period before calling
activation functions. Unavailable connections return an error string rather than
queueing commands. Both boards must use the current ESP32 command protocol.

## Game-facing functions

| Function | Behavior |
| --- | --- |
| `ActivateFingerprint()` | Release board 1 and select fingerprint. |
| `ReadFingerprint(callback)` | Send FP READ; callback receives matched ID or -1. |
| `EnrollFingerprint(id, callback)` | Send FP ENROLL; callback receives progress state or -1. |
| `CancelFingerprintOperation()` | Cancel pending read/enrollment; complete its result as Cancelled. |
| `ActivateKeypad()` | Release board 1 and start automatic press reporting. |
| `ActivateUltrasonic()` | Release board 2 and start automatic distance reports. |
| `ActivateJoystick()` | Release board 2 and start automatic X/Y reports. |
| `DeactivateFingerprint/Keypad/Ultrasonic/Joystick()` | Release that component if active or awaiting activation. |
| `DeactivateAll()` | Release both boards. |

Selection is confirmed by `PANEL ACTIVE`. Check `FingerprintKeypadActive` or
`UltrasonicJoystickActive` for ordinary component activation. Call fingerprint functions after FP activation is confirmed. One component
can run on each board simultaneously. Switching automatically sends RELEASE then
ACTIVATE on that board. Blocking ultrasonic sampling may delay acknowledgments.
Disabling the manager releases connected boards and unsubscribes from serial events.

## Readings and events

| Property | Event | Meaning |
| --- | --- | --- |
| `UltrasonicDistanceCm` | `UltrasonicUpdated(float)` | Nonnegative cm, capped at 125. `-1`, NaN, infinity and errors leave the old value unchanged. |
| `Joystick` | `JoystickUpdated(Vector2)` | Each axis independently mapped to -1..1 from 12-bit raw values. |
| `KeypadPosition` | `KeypadPressed(Vector2Int)` | `(column-1, 4-row)`: top-left `(0,0)` = physical row 4/column 1; bottom-right `(3,3)` = physical row 1/column 4. |
| `FingerprintId`, `FingerprintConfidence` | `FingerprintMatched(int,int)` | Recognized saved ID and confidence. |
| `LastStoredFingerprintId` | `FingerprintStored(int)` | Successful enrollment ID. |
| `HasFingerprintMatch` | `FingerprintNotMatched()` | No-match clears recognized identity to -1. |

`HasUltrasonicReading`, `HasJoystickReading`, `HasKeypadPress` and
`HasFingerprintMatch` track validity in the current activation. Readings are
retained for inspection after release, but validity flags clear. Subscribe to
events for repeated presses of the same key. Events run on Unity's main thread.
Unsubscribe your own listeners when disabling your game objects.

Joystick Centre X/Y are adjustable raw rest positions, default 2047.5. Each axis
maps 0 -> -1, centre -> 0, 4095 -> +1; no vector-length normalization or dead zone
is applied. Calibration does not repair incorrect wiring.

`LastFingerprintKeypadError` / `LastUltrasonicJoystickError` retain the latest
ESP error or command failure. Unknown lines, progress prompts, diagnostics,
malformed numbers and wrong-board readings do not throw or overwrite values.
Only the active component's readings are applied.

## Implementation and verification

- `Esp32SerialConnection.cs`: USB transport, `LineReceived` / `Disconnected` events,
  `TrySendCommand()` for callers that need send success/failure.
- `PanelSerialProtocol.cs`: parsing, bounds checking, grid mapping and normalization.
- `ComponentManager.cs`: board selection, command functions, values and events.
- Tools -> Mission Marshal -> Check Component Manager: hardware-free checks for
  parser failures, non-English numeric locale, routing and conversion rules.

Physical board connections and sensor readings still need end-to-end testing.

## Fingerprint Action callbacks

Activate fingerprint mode first and wait until `FingerprintKeypadActive` equals
`PanelSerialProtocol.Component.Fingerprint`, then call:

```csharp
manager.ReadFingerprint(id => Debug.Log("Read result: " + id));
manager.EnrollFingerprint(7, state => Debug.Log("Enrollment state: " + state));
```

These are separate examples: do not start enrollment while a read is pending.
There is no coroutine/operation framework or waiting loop. Serial messages invoke
stored `Action<int>` variables on Unity's main thread:

- `FingerprintReadCallback`: matched finger ID; **-1** for no match or failure.
- `FingerprintEnrollmentCallback`: **0** = place finger for first scan;
  **1** = remove finger; **2** = place the same finger for second scan;
  **3** = successfully stored; **-1** = failure at any point.

Each method stores its callback. Null callbacks are allowed. The read Action stays
assigned and is invoked whenever a valid read result arrives while FP is active.
The enrollment Action receives progress only during a pending enrollment.
Parameterless `ReadFingerprint()` and `EnrollFingerprint(id)` reuse their stored
Actions. You can assign the Action fields directly if preferred.

`IsReadingFingerprint` / `IsEnrollingFingerprint` expose whether a command is pending.
Only one read/enrollment is allowed at a time; a conflicting call reports -1 to
its supplied callback without replacing the running command's callback.
`CancelFingerprintOperation()` sends FP CANCEL. Errors, cancellation, component
release/switch, disconnect and manager disable report -1 to the pending callback.
Firmware step timeouts arrive as FP ERROR and also report -1. No additional Unity
timeout is added. A missing response does not block Unity; explicitly cancel or
retry if needed. Callback exceptions are logged without breaking the serial reader.
Enrollment stores templates on the sensor; an existing ID is replaced on success.

## Callback setters and cleanup

```csharp
manager.ActivateKeypad();
manager.SetKeypadCallback(position => Debug.Log(position));
// Later:
manager.DeactivateKeypad(); // Clears its callback to null.
```

Available setters (replace the existing listener; null clears it):

- `SetKeypadCallback(Action<Vector2Int>)`
- `SetJoystickCallback(Action<Vector2>)`
- `SetUltrasonicCallback(Action<float>)` (centimetres)
- `SetFingerprintReadCallback(Action<int>)` (finger ID or -1)
- `SetFingerprintEnrollmentCallback(Action<int>)` (states 0..3 or -1)

Register after requesting activation. Component deactivation, switching away,
remote release, board disconnect and manager disable clear the affected callbacks,
including legacy events for that component. A pending fingerprint operation is
notified with -1 before its callbacks are cleared. DeactivateAll clears everything.
The other board's callbacks are unaffected. Re-register callbacks on the next use.
