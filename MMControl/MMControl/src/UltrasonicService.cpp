/*
 * COMMANDS
 *   ACTIVATE ULTRASONIC -> initialise pins and continuously measure distance
 *   ULTRASONIC STATUS -> report pins and echo timeout while selected
 *   RELEASE / DISABLE / DEACTIVATE -> release pins and return panel to idle
 * OUTPUT: ULTRASONIC DISTANCE cm=12.34 echo_us=716
 *         ULTRASONIC ERROR echo_timeout (pulseIn returned zero)
 *
 * WIRING: TRIG -> GPIO33, ECHO -> GPIO32, shared GND. HC-SR04-style protocol.
 * Standard 5V HC-SR04: level-shift ECHO before GPIO32. Example divider:
 * ECHO -- 1k -- GPIO32 -- 2k -- GND. Check the sensor's supply specification.
 * Pins overlap keypad C1/C2; physically connected circuits may need isolation.
 *
 * FILE ROLE: intentionally BLOCKING ultrasonic measurement using pulseIn().
 * activate(): restore trigger OUTPUT/LOW and echo INPUT on every activation.
 * update(): generate a 10 us trigger, then block on pulseIn(ECHO, HIGH, timeout).
 * Print the result immediately afterwards. No interrupts, cooldown, or report
 * timer: the next loop can start the next measurement. Only the required 2 us
 * LOW / 10 us HIGH trigger pulse uses delayMicroseconds().
 * Serial commands are processed again after pulseIn returns, including on timeout;
 * RELEASE cannot interrupt a measurement already in progress. Timeout is 30 ms
 * by default; change UltrasonicEchoTimeoutUs in PanelConfig.h.
 * deactivate(): stop future measurements and return both pins to plain INPUT.
 * printStatus(): report settings without triggering an extra measurement.
 * Distance assumes 343 m/s sound speed, without temperature compensation.
 * HC-SR04 datasheets recommend >60 ms between triggers; this requested immediate
 * retrigger behavior may suffer interference from earlier echoes at short ranges.
 */
#include "UltrasonicService.h"
#include "PanelConfig.h"

namespace {
bool enabled = false;
}

namespace UltrasonicService {
void deactivate() {
  enabled = false;
  digitalWrite(PanelConfig::UltrasonicTrigger, LOW);
  pinMode(PanelConfig::UltrasonicTrigger, INPUT);
  pinMode(PanelConfig::UltrasonicEcho, INPUT);
}

void activate() {
  deactivate();
  digitalWrite(PanelConfig::UltrasonicTrigger, LOW);
  pinMode(PanelConfig::UltrasonicTrigger, OUTPUT);
  pinMode(PanelConfig::UltrasonicEcho, INPUT);
  enabled = true;
}

void update() {
  if (!enabled) return;
  digitalWrite(PanelConfig::UltrasonicTrigger, LOW);
  delayMicroseconds(2);
  digitalWrite(PanelConfig::UltrasonicTrigger, HIGH);
  delayMicroseconds(10);
  digitalWrite(PanelConfig::UltrasonicTrigger, LOW);

  // Deliberately block the main loop until a complete echo or timeout.
  const unsigned long width = pulseIn(PanelConfig::UltrasonicEcho, HIGH,
                                     PanelConfig::UltrasonicEchoTimeoutUs);
  if (width == 0) {
    Serial.printf("ULTRASONIC DISTANCE cm=-1 echo_us=%lu\n", width);
    return;
  }
  Serial.printf("ULTRASONIC DISTANCE cm=%.2f echo_us=%lu\n",
                width * 0.0343f / 2.0f, width);
  delay(60); // Allow echo to decay before next trigger; datasheet recommends >60 ms between triggers.
}

void printStatus() {
  Serial.printf("ULTRASONIC STATUS enabled=%u trig_pin=%d echo_pin=%d timeout_us=%lu\n",
    enabled, PanelConfig::UltrasonicTrigger, PanelConfig::UltrasonicEcho,
    static_cast<unsigned long>(PanelConfig::UltrasonicEchoTimeoutUs));
}
}
