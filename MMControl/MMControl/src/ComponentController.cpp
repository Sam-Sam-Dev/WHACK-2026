/*
 * COMMANDS HANDLED THROUGH THIS CONTROLLER
 *   ACTIVATE FP | ACTIVATE KEYPAD -> select a peripheral (idle only)
 *   RELEASE | DISABLE            -> stop work and return to NONE
 *   STATUS                       -> PANEL STATUS active=NONE / FP / KEYPAD
 * Commands are parsed in SerialCommands.cpp, not in this file.
 *   ACTIVATE JOYSTICK -> select joystick; DEACTIVATE is another RELEASE alias.
 *   ACTIVATE ULTRASONIC -> initialise trigger/echo pins and start distance reports.
 * Every activation reinitialises that module's hardware. Every release detaches
 * the old module BEFORE another can claim overlapping pins. This is software
 * ownership only: externally connected peripherals still need electrical isolation.
 *
 * FILE ROLE: single owner of component selection. Services do not select each
 * other, and main.cpp calls only this controller's update().
 * selected / active(): current selection, initially None.
 * name(): converts selection to its serial protocol label.
 * activate(): rejects changes until release; verifies FP or prepares keypad pins;
 * reports PANEL ACTIVE on success. Failed FP activation leaves the panel idle.
 * release(): cancels fingerprint work or releases keypad pins, then reports IDLE.
 * update(): calls exactly the selected service; no polling happens while idle.
 * printStatus(): reports selection, not continuous sensor connection health.
 *
 * TO EXTEND: add a Component enum value in ComponentController.h, then add its
 * name, activation, release and update branches here, plus its serial command.
 * An operation finishing or timing out does NOT release the selected component.
 * Releasing stops software polling; peripherals may remain electrically powered.
 */
#include "ComponentController.h"
#include "FingerprintService.h"
#include "KeypadService.h"
#include "JoystickService.h"
#include "UltrasonicService.h"
#include <Arduino.h>

namespace {
using Component = ComponentController::Component;
Component selected = Component::None;
const char* name(Component component) {
  switch (component) {
    case Component::Fingerprint: return "FP";
    case Component::Keypad: return "KEYPAD";
    case Component::Joystick: return "JOYSTICK";
    case Component::Ultrasonic: return "ULTRASONIC";
    default: return "NONE";
  }
}
}

namespace ComponentController {
Component active() { return selected; }

void activate(Component component) {
  if (selected != Component::None) {
    Serial.printf("PANEL ERROR active=%s release_first\n", name(selected));
    return;
  }
  if (component == Component::None) return;
  if (component == Component::Fingerprint && !FingerprintService::begin()) {
    Serial.println("PANEL ERROR activation_failed component=FP");
    return; // Failed activation leaves the panel idle.
  }
  if (component == Component::Keypad) KeypadService::activate();
  if (component == Component::Joystick) JoystickService::activate();
  if (component == Component::Ultrasonic) UltrasonicService::activate();
  selected = component;
  Serial.printf("PANEL ACTIVE component=%s\n", name(selected));
}

void release() {
  if (selected == Component::Fingerprint) FingerprintService::deactivate();
  if (selected == Component::Keypad) KeypadService::deactivate();
  if (selected == Component::Joystick) JoystickService::deactivate();
  if (selected == Component::Ultrasonic) UltrasonicService::deactivate();
  selected = Component::None;
  Serial.println("PANEL IDLE");
}

void update() {
  switch (selected) {
    case Component::Fingerprint: FingerprintService::update(); break;
    case Component::Keypad: KeypadService::update(); break;
    case Component::Joystick: JoystickService::update(); break;
    case Component::Ultrasonic: UltrasonicService::update(); break;
    case Component::None: break;
  }
}

void printStatus() { Serial.printf("PANEL STATUS active=%s\n", name(selected)); }
}
