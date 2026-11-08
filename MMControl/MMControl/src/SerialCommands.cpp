/*
 * COMMANDS (case-sensitive; spaces matter; send LF or CRLF)
 *   ACTIVATE FP | ACTIVATE KEYPAD -> select one component while idle
 *   RELEASE | DISABLE            -> release selection, available in every mode
 *   STATUS | HELP                -> panel status / command list, always available
 *   FP READ                      -> identify one finger
 *   FP ENROLL <id>               -> two-scan enrollment; decimal ID 1..capacity-1
 *   FP STATUS | FP CANCEL | FP CONNECT | FP HELP -> fingerprint tools
 *   KEYPAD DEBUG ON | KEYPAD DEBUG OFF | KEYPAD STATUS -> keypad diagnostics
 *   ACTIVATE JOYSTICK -> stream raw X/Y; JOYSTICK STATUS -> configuration
 *   DEACTIVATE -> alias of RELEASE, available in every mode
 *   ACTIVATE ULTRASONIC -> stream distance; ULTRASONIC STATUS -> configuration
 * All FP commands require ACTIVATE FP first; FP CANCEL leaves FP selected.
 * Example: ACTIVATE FP -> FP ENROLL 1 -> follow prompts -> RELEASE.
 *
 * FILE ROLE: USB line reader and command router; no peripheral scanning here.
 * listenForSerialCommands(): accumulates partial lines across loop() calls;
 * accepts at most 255 characters and reads at most 256 bytes per loop. Blank lines
 * are ignored. An oversized line is discarded entirely, preventing partial actions.
 * dispatch(): handles panel commands first, gates device commands by selection,
 * validates enrollment IDs, then delegates to the matching service function.
 * Unknown FP/activation commands produce errors; other text echoes only when idle.
 * Responses use PANEL / FP / KEYPAD prefixes for Unity to distinguish their source.
 *
 * TO EXTEND: add command validation here; put the actual hardware action in a
 * service and register its selection/update/release in ComponentController.cpp.
 * USB Serial is shared with Unity: close the serial monitor before Unity connects.
 */
#include "SerialCommands.h"
#include "FingerprintService.h"
#include "ComponentController.h"
#include "KeypadService.h"
#include "JoystickService.h"
#include "UltrasonicService.h"
#include <Arduino.h>
#include <cstring>

namespace {
void dispatch(const char* command) {
  using Component = ComponentController::Component;
  // Panel-level commands always work, even during an enrollment or key scan.
  if (strcmp(command, "RELEASE") == 0 || strcmp(command, "DISABLE") == 0 ||
      strcmp(command, "DEACTIVATE") == 0) {
    ComponentController::release();
    return;
  }
  if (strcmp(command, "ACTIVATE FP") == 0) {
    ComponentController::activate(Component::Fingerprint);
    return;
  }
  if (strcmp(command, "ACTIVATE KEYPAD") == 0) {
    ComponentController::activate(Component::Keypad);
    return;
  }
  if (strcmp(command, "ACTIVATE JOYSTICK") == 0) {
    ComponentController::activate(Component::Joystick);
    return;
  }
  if (strcmp(command, "STATUS") == 0) { ComponentController::printStatus(); return; }
  if (strcmp(command, "ACTIVATE ULTRASONIC") == 0) {
    ComponentController::activate(Component::Ultrasonic);
    return;
  }
  if (strcmp(command, "HELP") == 0) {
    Serial.println("PANEL COMMANDS: ACTIVATE FP | ACTIVATE KEYPAD | ACTIVATE JOYSTICK | ACTIVATE ULTRASONIC | RELEASE | DISABLE | DEACTIVATE | STATUS | FP HELP | KEYPAD STATUS | KEYPAD DEBUG ON/OFF | JOYSTICK STATUS | ULTRASONIC STATUS");
    return;
  }
  if (strncmp(command, "ACTIVATE", 8) == 0) {
    Serial.println("PANEL ERROR unknown_component use_HELP");
    return;
  }
  // Never forward commands to an inactive peripheral.
  if (strncmp(command, "ULTRASONIC", 10) == 0) {
    if (ComponentController::active() != Component::Ultrasonic)
      Serial.println("PANEL ERROR component_not_active required=ULTRASONIC");
    else if (strcmp(command, "ULTRASONIC STATUS") == 0) UltrasonicService::printStatus();
    else Serial.println("ULTRASONIC ERROR unknown_command use_HELP");
    return;
  }
  if (strncmp(command, "JOYSTICK", 8) == 0) {
    if (ComponentController::active() != Component::Joystick)
      Serial.println("PANEL ERROR component_not_active required=JOYSTICK");
    else if (strcmp(command, "JOYSTICK STATUS") == 0) JoystickService::printStatus();
    else Serial.println("JOYSTICK ERROR unknown_command use_HELP");
    return;
  }
  if (strncmp(command, "KEYPAD", 6) == 0) {
    if (ComponentController::active() != Component::Keypad)
      Serial.println("PANEL ERROR component_not_active required=KEYPAD");
    else if (strcmp(command, "KEYPAD DEBUG ON") == 0) KeypadService::setDebug(true);
    else if (strcmp(command, "KEYPAD DEBUG OFF") == 0) KeypadService::setDebug(false);
    else if (strcmp(command, "KEYPAD STATUS") == 0) KeypadService::printStatus();
    else Serial.println("KEYPAD ERROR unknown_command use_HELP");
    return;
  }
  if (strncmp(command, "FP", 2) == 0 &&
      ComponentController::active() != Component::Fingerprint) {
    Serial.println("PANEL ERROR component_not_active required=FP");
    return;
  }
  if (strcmp(command, "FP READ") == 0) FingerprintService::readFinger();
  else if (strcmp(command, "FP STATUS") == 0) FingerprintService::printStatus();
  else if (strcmp(command, "FP CANCEL") == 0) FingerprintService::cancel();
  else if (strcmp(command, "FP CONNECT") == 0) {
    FingerprintService::cancel();
    FingerprintService::begin();
  } else if (strncmp(command, "FP ENROLL ", 10) == 0) {
    // Strict decimal parsing: never turn malformed input into a storage slot.
    const char* digits = command + 10;
    uint32_t id = 0;
    if (!*digits) { Serial.println("FP ERROR usage=FP_ENROLL_<id>"); return; }
    for (const char* p = digits; *p; ++p) {
      if (*p < '0' || *p > '9' || id > 6553 ||
          (id == 6553 && *p > '5')) {
        Serial.println("FP ERROR id_must_be_decimal_1_to_capacity_minus_1");
        return;
      }
      id = id * 10 + (*p - '0');
    }
    FingerprintService::storeFinger(static_cast<uint16_t>(id));
  } else if (strcmp(command, "FP HELP") == 0) {
    Serial.println("FP COMMANDS: FP READ | FP ENROLL <id> | FP STATUS | FP CANCEL | FP CONNECT");
  } else if (strncmp(command, "FP", 2) == 0) {
    Serial.println("FP ERROR unknown_command use_FP_HELP");
  } else {
    if (ComponentController::active() == Component::None)
      Serial.println(command); // Preserve the original Unity echo test while idle.
    else Serial.println("PANEL ERROR unknown_command_for_active_component");
  }
}
}

void listenForSerialCommands() {
  static char command[256];
  static size_t length = 0;
  static bool overflow = false;
  for (size_t n = 0; n < 256 && Serial.available(); ++n) {
    const char c = static_cast<char>(Serial.read());
    if (c == '\n') {
      command[length] = '\0';
      if (overflow) Serial.println("ERROR command_too_long");
      else if (length > 0) dispatch(command);
      length = 0;
      overflow = false;
    } else if (c != '\r' && !overflow) {
      if (length < sizeof(command) - 1) command[length++] = c;
      else overflow = true; // Discard the whole line, not a partial command.
    }
  }
}
