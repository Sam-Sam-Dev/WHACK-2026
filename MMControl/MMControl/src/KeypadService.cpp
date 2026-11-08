/*
 * COMMANDS
 *   ACTIVATE KEYPAD -> start listening; no further keypad command is required
 *   RELEASE / DISABLE -> stop scanning and release all eight pins to INPUT
 *   STATUS -> report selection (handled by ComponentController)
 *   KEYPAD DEBUG ON / KEYPAD DEBUG OFF -> log debounced matrix changes
 *   KEYPAD STATUS -> current raw mask, accepted mask and pin assignments
 * OUTPUT: KEYPAD PRESS row=2 col=3; coordinates are ONE-BASED, not key symbols.
 * Multiple keys: KEYPAD ERROR multiple_keys release_all_keys.
 *
 * WIRING: R1..R4 = GPIO4,16,18,19; C1..C4 = GPIO32,33,23,27.
 * Passive switch matrix only; use labelled rows/columns, since cable order varies.
 * GPIO21/22 remain free for I2C. Pin order/timing live in PanelConfig.h.
 *
 * FILE ROLE: scan and debounce one keypad without blocking on a user's action.
 * scanMatrix(): drives one row LOW at a time, reads pulled-up columns, then
 * releases that row to INPUT_PULLUP. Returns a 16-bit mask:
 * bit (row*4+col) identifies a switch. Weak pull-ups keep inactive rows defined;
 * configurable settling delays allow columns to recover between row scans.
 * activate(): enables column pull-ups and resets state; an already-held key must
 * be released before a press can be reported. deactivate(): stops and releases pins.
 * update(): samples every 5 ms, accepts a state after 30 ms stable, then emits
 * one message per press. Both values are configurable; no held-key auto-repeat.
 * candidate/changedAt = pending debounce state; stable = accepted state;
 * armed = a clean all-keys-up state has occurred, allowing the next press.
 * Multi-key scans are rejected because diode-less matrices may produce ghosts.
 * This service runs only while selected by ComponentController.
 */
#include "KeypadService.h"
#include "PanelConfig.h"

namespace {
bool enabled = false;
bool debug = false;
bool armed = false;
uint16_t candidate = 0;
uint16_t stable = 0;
uint32_t changedAt = 0;
uint32_t lastScan = 0;

void printMask(const char* label, uint16_t mask) {
  Serial.printf("KEYPAD %s mask=0x%04X", label, mask);
  for (int index = 0; index < 16; ++index) {
    if (mask & (1U << index))
      Serial.printf(" r%dc%d", index / 4 + 1, index % 4 + 1);
  }
  Serial.println("");
}

uint16_t scanMatrix() {
  uint16_t keys = 0;
  for (int row = 0; row < 4; ++row) {
    // Only one row drives LOW. Other rows are weakly pulled-up inputs, never
    // HIGH outputs: simultaneous keys cannot short two GPIO outputs together.
    digitalWrite(PanelConfig::KeypadRows[row], LOW);
    pinMode(PanelConfig::KeypadRows[row], OUTPUT);
    delayMicroseconds(PanelConfig::KeypadSettleUs);
    for (int col = 0; col < 4; ++col) {
      if (digitalRead(PanelConfig::KeypadColumns[col]) == LOW)
        keys |= static_cast<uint16_t>(1U << (row * 4 + col));
    }
    pinMode(PanelConfig::KeypadRows[row], INPUT_PULLUP);
    // A held switch connects the column to this row; let it recover HIGH before
    // scanning the next row, or one key may appear in several successive rows.
    delayMicroseconds(PanelConfig::KeypadSettleUs);
  }
  return keys;
}
}

namespace KeypadService {
void deactivate() {
  enabled = false;
  debug = false;
  // Release all pins when inactive; the keypad is no longer scanned or driven.
  for (int pin : PanelConfig::KeypadRows) pinMode(pin, INPUT);
  for (int pin : PanelConfig::KeypadColumns) pinMode(pin, INPUT);
}

void activate() {
  deactivate();
  for (int pin : PanelConfig::KeypadRows) pinMode(pin, INPUT_PULLUP);
  for (int pin : PanelConfig::KeypadColumns) pinMode(pin, INPUT_PULLUP);
  enabled = true;
  armed = false; // Require a clean release, so a held key cannot trigger activation.
  stable = candidate = 0xFFFF;
  changedAt = lastScan = millis();
}

void update() {
  if (!enabled) return;
  const uint32_t now = millis();
  if (now - lastScan < PanelConfig::KeypadScanIntervalMs) return;
  lastScan = now;
  const uint16_t keys = scanMatrix();
  if (keys != candidate) { candidate = keys; changedAt = now; }
  if (now - changedAt < PanelConfig::KeypadDebounceMs || candidate == stable) return;
  stable = candidate;
  if (debug) printMask("RAW", stable);
  if (stable == 0) { armed = true; return; }
  if (!armed) return;
  armed = false; // No repeats until ALL buttons have been released.
  if ((stable & (stable - 1)) != 0) {
    // Diode-less keypads can ghost. Never invent a coordinate for a multi-key scan.
    Serial.println("KEYPAD ERROR multiple_keys release_all_keys");
    printMask("DETECTED", stable);
    return;
  }
  int index = 0;
  while ((stable & (1U << index)) == 0) ++index;
  Serial.printf("KEYPAD PRESS row=%d col=%d\n", index / 4 + 1, index % 4 + 1);
}

void setDebug(bool value) {
  debug = value;
  Serial.printf("KEYPAD DEBUG %s\n", debug ? "ON" : "OFF");
}

void printStatus() {
  Serial.printf("KEYPAD STATUS enabled=%u debug=%u\n", enabled, debug);
  if (!enabled) return;
  printMask("RAW", scanMatrix());
  printMask("STABLE", stable);
  Serial.printf("KEYPAD PINS rows=%d,%d,%d,%d cols=%d,%d,%d,%d settle_us=%lu\n",
    PanelConfig::KeypadRows[0], PanelConfig::KeypadRows[1],
    PanelConfig::KeypadRows[2], PanelConfig::KeypadRows[3],
    PanelConfig::KeypadColumns[0], PanelConfig::KeypadColumns[1],
    PanelConfig::KeypadColumns[2], PanelConfig::KeypadColumns[3],
    static_cast<unsigned long>(PanelConfig::KeypadSettleUs));
}
}
