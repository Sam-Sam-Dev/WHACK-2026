/*
 * COMMANDS
 *   ACTIVATE JOYSTICK -> initialise ADC pins and start automatic X/Y reporting
 *   RELEASE / DISABLE / DEACTIVATE -> stop reporting and return panel to idle
 *   JOYSTICK STATUS -> report pins and reporting interval while selected
 * OUTPUT: JOYSTICK POSITION x=2048 y=2048 (raw 12-bit values, 0..4095).
 * Reports every 50 ms, including when stationary; no centre calibration assumed.
 *
 * WIRING: VRx -> GPIO4, VRy -> GPIO13, VCC -> 3.3V, GND -> common GND.
 * SW/button is not connected or read. These are ADC2 pins: do not run Wi-Fi
 * while sampling them on classic ESP32. Pins/timing live in PanelConfig.h.
 * GPIO4 is also keypad R1. Software reinitialises pins when switching modes,
 * but cannot isolate two circuits physically connected to the same signal.
 * Disconnect conflicting signals or use a hardware multiplexer when necessary.
 *
 * FILE ROLE: owns joystick pin setup, averaged analog readings and serial output.
 * activate(): remove digital pull-ups/output modes, reset ADC resolution and
 * attenuation EVERY activation, discard initial readings, reset the report timer.
 * deactivate(): stop reporting and restore plain high-impedance inputs.
 * readAxis(): average configured sample count; raw values are not voltages.
 * update(): sample/report only when active and the reporting interval expires.
 * printStatus(): configuration report; does not take an extra ADC reading.
 * Controller owns selection; this service never activates other peripherals.
 */
#include "JoystickService.h"
#include "PanelConfig.h"

namespace {
bool enabled = false;
uint32_t lastReport = 0;
int readAxis(int pin) {
  uint32_t total = 0;
  for (int i = 0; i < PanelConfig::JoystickSamples; ++i) total += analogRead(pin);
  return total / PanelConfig::JoystickSamples;
}
static_assert(PanelConfig::JoystickSamples > 0, "JoystickSamples must be positive");
}

namespace JoystickService {
void deactivate() {
  enabled = false;
  pinMode(PanelConfig::JoystickX, INPUT);
  pinMode(PanelConfig::JoystickY, INPUT);
}

void activate() {
  deactivate(); // Remove any digital mode/pull-up left by the previous module.
  analogReadResolution(12);
  analogSetPinAttenuation(PanelConfig::JoystickX, ADC_11db);
  analogSetPinAttenuation(PanelConfig::JoystickY, ADC_11db);
  analogRead(PanelConfig::JoystickX);
  analogRead(PanelConfig::JoystickY);
  lastReport = millis();
  enabled = true;
}

void update() {
  if (!enabled || millis() - lastReport < PanelConfig::JoystickReportIntervalMs) return;
  lastReport = millis();
  
  const int x = readAxis(PanelConfig::JoystickX);
  const int y = readAxis(PanelConfig::JoystickY);
  Serial.printf("JOYSTICK POSITION x=%d y=%d\n", x, y);
}

void printStatus() {
  Serial.printf("JOYSTICK STATUS enabled=%u x_pin=%d y_pin=%d interval_ms=%lu\n",
    enabled, PanelConfig::JoystickX, PanelConfig::JoystickY,
    static_cast<unsigned long>(PanelConfig::JoystickReportIntervalMs));
}
}
