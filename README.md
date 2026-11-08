# WHACK 2026 — Mission Marshal

This repository contains the Unity game and its ESP32 control panel firmware.

## Unity game

Open `MissionMarshal` in Unity Hub using **Unity 6000.3.23f1**. Unity will restore packages and regenerate its local cache when first opened. Assets, their metadata, packages, and project settings are included.

## Microcontroller firmware

Open `MMControl/MMControl` with PlatformIO. The project uses the Arduino framework on an ESP32 (`denky32`) and a USB serial baud rate of 115200.

Build with `pio run` and upload with `pio run --target upload` from that directory. See [the control panel documentation](MMControl/MMControl/README.md) for wiring, configuration, and serial commands.

Generated caches, build outputs, local editor settings, and installer files are excluded from version control.
