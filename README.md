# WHACK 2026 — Mission Marshal

A cooperative spaceship game played through a physical ESP32 control panel. A wormhole has scattered the crew across separate universes, but they still share the controls of one ship. Work together, answer emergency alerts, and keep the ship alive long enough to reach its destination.

## The prompt: New Worlds

Mission Marshal was built around the hackathon prompt **“New Worlds.”** Our idea takes that literally: each crew member is stranded in a different universe after the ship passes through a wormhole. Those worlds have their own visual identity and different rules for the shared controls.

The physical panel is the crew's common link. When an alert calls for a particular crew member, that person must identify themselves with their fingerprint and take control in their universe. Exploring new worlds means adapting to how the same familiar controls behave in each one.

## The idea

We wanted the spaceship's control panel to exist outside the screen. Instead of clicking through repairs, players scan a real fingerprint sensor, press a physical keypad, steer with a joystick, and move their hand above a distance sensor.

Crew identity matters to the gameplay: each crew member has a distinct colour, a realm appearance, a shuffled keypad layout, and a joystick direction rule. These input rules are assigned at the beginning of the mission and remain consistent for that crew during the run. The challenge is to respond quickly while remembering whose world you are operating in.

## How to play

1. **Register the crew.** Each player enrols their fingerprint during setup.
2. **Begin the journey.** The ship follows a generated route through space towards a destination planet.
3. **Respond to an alert.** The ship pauses, a lightning effect signals the disruption, and the view switches into the requested crew member's realm.
4. **Take control.** That crew member must scan their registered finger before the timer expires.
5. **Restore the ship.** Some alerts resolve after authentication; others require a physical minigame. Finish the task to return to normal flight.
6. **Reach the destination.** Failures cost hull health. Lose all hearts and the ship is destroyed; reach the end of the route to complete the mission.

Minigames become more frequent as the journey progresses. Timers, task difficulty, challenge spacing, and starting health can be adjusted in Unity's Inspector.

## Physical minigames

| Control | Challenge |
| --- | --- |
| Fingerprint sensor | Identify the crew member requested by the ship and authorise their takeover. |
| 4×4 keypad | Enter the displayed sequence using that crew's shuffled mapping of `0–9` and `A–F`. Work quickly and avoid repeated mistakes. |
| Joystick | Move left and right to dodge falling asteroids. Depending on the crew, the joystick direction may be normal or inverted. |
| Ultrasonic sensor | Calibrate the engine's “gravity pads” by holding your hand at the target height. Live distance and colour feedback help you find and maintain the accepted range. |

## Hardware and software

- **Unity 6000.3.23f1 / C#:** spaceship movement, crew realms, mission events, minigames, UI, and audio.
- **ESP32 / Arduino / PlatformIO:** sensor handling, keypad scanning, and serial command processing.
- **Physical inputs:** R307S fingerprint sensor, 4×4 matrix keypad, analog joystick, and HC-SR04-compatible ultrasonic sensor.
- **USB serial at 115200 baud:** connects Unity to the control hardware.

The Unity integration uses **two ESP32 connections**: one for fingerprint/keypad and one for ultrasonic/joystick. Each board activates one peripheral at a time. The firmware project contains the services for all four peripherals.

## Repository layout

| Path | Contents |
| --- | --- |
| `MissionMarshal/` | Unity project, including assets, scenes, packages, and project settings. |
| `MMControl/MMControl/` | ESP32 firmware, PlatformIO configuration, and hardware documentation. |

## Run the Unity game

1. Clone this repository.
2. Add `MissionMarshal` to Unity Hub and open it with **Unity 6000.3.23f1**. Unity will restore packages and regenerate its local cache on first launch.
3. Connect the two ESP32 boards and configure the USB port names on their `Esp32SerialConnection` components.
4. Confirm the fingerprint/keypad and ultrasonic/joystick connections are assigned to `ComponentManager`.
5. Open `Assets/Scenes/Start.unity`, enter Play mode, and register the crew to begin.

Gameplay uses the physical hardware; the minigames do not include a keyboard fallback.

## Build and upload the firmware

Open `MMControl/MMControl` with PlatformIO. The project uses the Arduino framework on an ESP32 (`denky32`). From that directory:

```sh
pio run
pio run --target upload
```

Check the pin assignments in `include/PanelConfig.h` against your wiring before uploading. Close the PlatformIO serial monitor before connecting from Unity so that both applications do not compete for the same USB port.

See [the control panel documentation](MMControl/MMControl/README.md) for wiring notes and serial commands. The current pin assignments are defined in `PanelConfig.h`.

## More project documentation

- [Unity component and hardware integration](MissionMarshal/Assets/ComponentManagerGuide.md)
- [Mission tasks and gameplay settings](MissionMarshal/Assets/MissionTasksGuide.md)
- [Firmware and hardware guide](MMControl/MMControl/README.md)

Generated caches, build outputs, local editor settings, and installer files are excluded from version control. Third-party assets and their included licence files remain in the Unity project.
