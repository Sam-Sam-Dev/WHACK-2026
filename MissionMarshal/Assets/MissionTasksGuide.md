# Mission tasks

SampleScene's GameManager references MissionTaskController and MissionTaskDisplay.
After correct crew authentication, the combined minigame chance ramps linearly
from 50% at route start to 90% at route end. The remaining chance is normal
flight; keypad, joystick and ultrasonic share the minigame chance equally:
normal flight, keypad, asteroid avoidance, or ultrasonic calibration. The ship
remains paused through the task and any remaining Zap. Success/failure ends the
task, releases its peripheral and resumes the existing challenge flow.

## Rules

- Keypad: 8–12 symbols, one second per symbol in the total countdown. The crew's
  mapped keypad is shown. Every press consumes the next symbol and colours it
  green/red. A second wrong press or timeout fails; finishing with <=1 error wins.
- Joystick: use the crew-transformed horizontal input. Survive 5–10 seconds with
  fewer than two asteroid hits. A brief hit cooldown prevents one overlap from
  counting twice. Normal, swapped, inverted, and combined controls are possible.
- Ultrasonic: random target 15–35 cm, tolerance ±3 cm. Stay within tolerance for
  one continuous second before the six-second deadline. Missing/stale readings
  reset the hold. Text reports TOO LOW, TOO HIGH or PERFECT.

Task timers start after the ESP acknowledges activation. Connection/activation
has its own eight-second timeout. Failed keys/collisions/tasks spawn the existing
GameManager error-sound prefab. Existing component setters clear on deactivation.

## Presentation and art slots

MissionTaskDisplay builds an isolated orthographic camera and world-space UI
on layer 31, rendering into a 1000x700 RenderTexture. A screen-overlay RawImage
shows the texture during tasks. The main camera excludes the task layer.
Layer 31 should be reserved for the task stage.

On MissionTaskDisplay assign:

- Ship Sprite (otherwise a cyan rectangle).
- Asteroid Sprite (otherwise a brown rectangle).
- Ultrasonic Guide (optional; left unassigned as requested).

No art lookup or generated artwork is required. The display, task controller and
GameManager have separate responsibilities. Input remains hardware-driven; no
keyboard fallback has been added.

For a chosen task during development, call
`missionTasks.StartTask(1, gameManager, crewId, success => { ... });`
where 0=normal, 1=keypad, 2=joystick, 3=ultrasonic. Normal gameplay uses StartRandom.


## Orientation, distance feedback and hull health

The keypad display top-left is physical row 4/column 1 (grid 0,0), and bottom-right
is physical row 1/column 4 (grid 3,3). Display order and input validation use the
same crew layout. Ultrasonic has a large central live-distance label, red when
outside target ±3 cm and green when perfect.

ShipHealthUI shows three red square placeholders at the top left; assign its Heart
Sprite to replace them. A task failure (including takeover timeout) removes one
heart, spawns GameManager's Failure Spark Prefab (currently the existing Zap), then
restores normal realm volumes. The third strike stops progression and displays
SHIP DESTROYED. Successful tasks restore normal without costing health.


## Destination, realms and sound design

Normal recovery now displays a short stabilisation step before completing. Real
minigames keep their crew realm until completion. Both success and failure return
to normal; failed tasks hide the task screen so the camera-attached Zap is visible.

RandomCurvedPath has Destination Prefab and Destination Offset fields. SampleScene
uses planet_continental_clouds, 20 world units past the last waypoint in the end
tangent direction. Regeneration replaces the previous destination. End-of-path
progress wins the mission, stops the ship and shows MISSION COMPLETE.

ShipStatusFeed displays faint, fading status messages during flight only. Edit its
Messages/Interval fields; assign a morse/radio prefab to the Telemetry sound slot.

ShipSoundEvents on GameManager exposes optional AudioClip slots for:
MissionStart, Zap, RealmEntered, FingerprintPrompt, FingerprintAccepted,
FingerprintRejected, TaskStarted, KeyCorrect, KeyWrong, AsteroidImpact,
GravityPerfect, TaskSuccess, TaskFailure, HeartLost, NormalRealm, Telemetry,
Victory, ShipDestroyed and TimerWarning (once in the final two seconds).
Empty slots are silent. Each assigned clip creates a temporary GameObject with
an AudioSource; it is destroyed after actual playback finishes. Volume and pitch
are configurable per slot. Existing wrong-finger sound prefab remains as the fallback
error feedback; avoid assigning duplicate sounds to both if they overlap.

Asteroid spawning waits 0.5 seconds after joystick activation is acknowledged.
Each rock begins fully above the clipped playfield and enters through its top edge.

## Inspector difficulty controls

On MissionTaskController:
- Outcome odds: Minigame Chance At Start **0.7** (30% fingerprint-only), At End
  **0.9** (10% fingerprint-only). Linearly interpolated over route progress.
- Minigame Weights: X keypad, Y joystick, Z ultrasonic. Defaults 1/1/1; zero
  disables that game. All zero safely selects normal recovery.
- Shared: activation timeout, normal recovery/result duration, low-time warning.
- Keypad: minimum/maximum sequence length, seconds per character, mistake limit.
- Asteroids: survival duration range, first-spawn delay, spawn interval, speed,
  ship speed/size, asteroid size, edge/bottom margins, hit limit, hit grace time.
- Ultrasonic: target range, tolerance, task duration, continuous hold duration,
  maximum reading age. Prompt text and hold bar follow these settings.

GameManager also exposes Challenge Progress Gap, Challenge Result Seconds and
Starting Hearts (1–20), in addition to takeover time, read interval and Zap duration.
Unsafe nonpositive durations/counts are clamped; increasing a range minimum beyond
its maximum brings the maximum up to that minimum. Sprites, sound clips, path
population settings and telemetry messages/intervals remain independently editable.

## Pitch/readability defaults

Task instructions are visible for 2 seconds before scoring/countdown starts. Early
key presses during this beat are ignored. Countdown bars now drain left-to-right
and have a numeric TIME LEFT label. Headings, code characters and keypad labels
are enlarged with automatic font fitting. Ultrasonic target and accepted range are
shown separately from live readings; asteroid instructions name the crew's axis rules.

SampleScene demo timing: takeover 8s; keypad 1.5s/character; ultrasonic 8s;
asteroid survival 7–9s; task result feedback 1.4s. All remain Inspector settings.
Failures show a hull-hit banner with remaining hearts while the lightning plays.


CrewIdentity provides a fixed colour for each crew ID in registration, takeover
prompts, and task badges. It uses the same palette across scenes, not new random
colours per round. Keypad coordinates are converted exactly once by the serial
parser at ComponentManager reception: x=column-1, y=4-row. All later code indexes
layout[y*4+x] directly, with no flips or random failure checks. Joystick data stays
signed; crew rules only swap axes and/or negate both axes, applied once per sample.
