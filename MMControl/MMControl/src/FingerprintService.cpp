/*
 * COMMANDS (send ACTIVATE FP first; USB Serial is 115200 baud)
 *   FP READ        -> wait for finger, then FP MATCH id=N confidence=N / FP NO_MATCH
 *   FP ENROLL <id> -> first scan, remove finger, second scan, then FP STORED id=N
 *   FP STATUS      -> last-known connection, busy state and template capacity
 *   FP CANCEL      -> cancel operation; keep FP selected
 *   FP CONNECT     -> cancel operation and retry sensor verification
 *   FP HELP        -> command list
 *   RELEASE / DISABLE -> cancel and return panel to idle (controller handles this)
 * IDs: 1..capacity-1; slot 0 is reserved. Saving an existing ID replaces its print.
 * Templates persist in SENSOR flash across power loss. Each user step allows 15s.
 *
 * WIRING/SETTINGS: R307S TX -> GPIO25 (ESP RX), RX -> GPIO26 (ESP TX),
 * VCC -> 5V, GND -> shared GND. UART2 at 57600; USB UART0 stays independent.
 * Change pins, password and timing in PanelConfig.h. GPIO2/D2 goes HIGH only
 * after verification + parameters succeed; LOW at init or detected UART failure.
 * Connection status is last-known, not a background heartbeat.
 *
 * FILE ROLE / FUNCTION MAP
 * sensorSerial/sensor: UART and Adafruit library instances, owned only here.
 * state: Idle -> Read, or FirstScan -> RemoveFinger -> SecondScan -> Idle.
 * begin(): initialise UART with explicit pins, verify password, read capacity.
 * readFinger()/storeFinger(id): validate readiness and START an operation.
 * update(): advance the state machine; extract features, search or combine/save.
 * cancel()/printStatus(): stop work / report state without starting a scan.
 * canStart(): reject disconnected/busy requests; setState(): prompt + step timer.
 * errorName()/fail(): explain library errors, end operation and invalidate UART
 * readiness when appropriate. Errors include stage, reason and hexadecimal code.
 *
 * Waiting for a finger uses timed polling, not a busy waiting loop. Individual
 * Adafruit calls remain synchronous and can block for their UART timeout.
 * Startup probes this service for D2; afterwards only the selected FP mode polls.
 * An operation finishing does not release selection; send RELEASE when done.
 * deactivate(): cancel, detach UART2 and return data pins to INPUT. D2 retains
 * last-known health. begin() reclaims/reinitialises the UART on every activation.
 */
#include "FingerprintService.h"
#include "PanelConfig.h"
#include <Adafruit_Fingerprint.h>

namespace {
HardwareSerial sensorSerial(2); // Keep USB Serial free for Unity commands.
Adafruit_Fingerprint sensor(&sensorSerial, PanelConfig::FingerprintPassword);
enum class State { Idle, Read, FirstScan, RemoveFinger, SecondScan };
State state = State::Idle;
bool connected = false;
bool uartStarted = false;
uint16_t enrollmentId = 0;
uint32_t stepStarted = 0;
uint32_t lastPoll = 0;

const char* errorName(uint8_t code) {
  switch (code) {
    case FINGERPRINT_PACKETRECIEVEERR: return "communication_error";
    case FINGERPRINT_TIMEOUT: return "sensor_timeout";
    case FINGERPRINT_IMAGEFAIL: return "image_capture_failed";
    case FINGERPRINT_IMAGEMESS: return "image_too_messy";
    case FINGERPRINT_FEATUREFAIL: return "not_enough_fingerprint_detail";
    case FINGERPRINT_INVALIDIMAGE: return "invalid_image";
    case FINGERPRINT_ENROLLMISMATCH: return "scans_do_not_match";
    case FINGERPRINT_BADLOCATION: return "invalid_storage_slot";
    case FINGERPRINT_FLASHERR: return "flash_write_failed";
    default: return "sensor_error";
  }
}

void fail(const char* stage, uint8_t code) {
  Serial.printf("FP ERROR stage=%s reason=%s code=0x%02X\n", stage, errorName(code), code);
  state = State::Idle;
  // A lost UART connection invalidates readiness; image quality errors do not.
  if (code == FINGERPRINT_PACKETRECIEVEERR || code == FINGERPRINT_TIMEOUT) {
    connected = false;
    digitalWrite(PanelConfig::SensorReadyLed, LOW);
  }
}

void setState(State next, const char* prompt) {
  state = next;
  stepStarted = millis(); // Each enrollment step gets its own timeout.
  Serial.println(prompt);
}

bool canStart() {
  if (!connected) { Serial.println("FP ERROR sensor_not_connected"); return false; }
  if (state != State::Idle) { Serial.println("FP ERROR busy use_FP_CANCEL"); return false; }
  return true;
}
}

namespace FingerprintService {
bool begin() {
  deactivate(); // Drop previous UART routing before reclaiming pins.
  pinMode(PanelConfig::SensorReadyLed, OUTPUT);
  digitalWrite(PanelConfig::SensorReadyLed, LOW);
  state = State::Idle;
  connected = false;
  // Initialise UART directly to preserve explicit ESP32 pin assignments;
  // do not call sensor.begin(), which also initialises HardwareSerial.
  sensorSerial.begin(PanelConfig::FingerprintBaud, SERIAL_8N1,
                     PanelConfig::FingerprintRx, PanelConfig::FingerprintTx);
  uartStarted = true;
  delay(250); // R307S needs about 200 ms to initialise after power-on.
  if (!sensor.verifyPassword()) {
    Serial.println("FP ERROR connection_failed check_power_wiring_baud_password");
    deactivate();
    return false;
  }
  uint8_t result = sensor.getParameters();
  if (result != FINGERPRINT_OK) { fail("parameters", result); deactivate(); return false; }
  connected = true;
  digitalWrite(PanelConfig::SensorReadyLed, HIGH);
  Serial.printf("FP READY capacity=%u\n", sensor.capacity);
  return true;
}

void deactivate() {
  if (state != State::Idle) cancel();
  state = State::Idle;
  connected = false;
  if (uartStarted) { sensorSerial.end(); uartStarted = false; }
  pinMode(PanelConfig::FingerprintRx, INPUT);
  pinMode(PanelConfig::FingerprintTx, INPUT);
}

void readFinger() {
  if (canStart()) setState(State::Read, "FP READ place_finger");
}

void storeFinger(uint16_t id) {
  if (!canStart()) return;
  // Expose slots 1..capacity-1 consistently; slot zero is reserved by this app.
  if (id == 0 || id >= sensor.capacity) {
    Serial.printf("FP ERROR invalid_id allowed=1-%u\n", sensor.capacity - 1);
    return;
  }
  enrollmentId = id;
  Serial.printf("FP ENROLL id=%u existing_slot_will_be_replaced_on_success\n", id);
  setState(State::FirstScan, "FP ENROLL place_finger_first_scan");
}

void cancel() {
  if (state == State::Idle) { Serial.println("FP CANCEL idle"); return; }
  state = State::Idle;
  Serial.println("FP CANCELLED");
}

void printStatus() {
  Serial.printf("FP STATUS connected=%u busy=%u capacity=%u\n",
                connected, state != State::Idle, connected ? sensor.capacity : 0);
}

void update() {
  if (state == State::Idle) return;
  const uint32_t now = millis();
  if (now - stepStarted >= PanelConfig::StepTimeoutMs) {
    state = State::Idle;
    Serial.println("FP ERROR finger_step_timeout");
    return;
  }
  if (now - lastPoll < PanelConfig::PollIntervalMs) return;
  lastPoll = now;
  uint8_t result = sensor.getImage();
  if (state == State::RemoveFinger) {
    if (result == FINGERPRINT_NOFINGER)
      setState(State::SecondScan, "FP ENROLL place_same_finger_second_scan");
    else if (result != FINGERPRINT_OK) fail("remove_finger", result);
    return;
  }
  if (result == FINGERPRINT_NOFINGER) return;
  if (result != FINGERPRINT_OK) { fail("capture", result); return; }
  result = sensor.image2Tz(state == State::SecondScan ? 2 : 1);
  if (result != FINGERPRINT_OK) { fail("extract_features", result); return; }

  if (state == State::Read) {
    // fingerSearch uses the reported capacity, unlike fixed-range fast search.
    result = sensor.fingerSearch();
    if (result == FINGERPRINT_OK)
      Serial.printf("FP MATCH id=%u confidence=%u\n", sensor.fingerID, sensor.confidence);
    else if (result == FINGERPRINT_NOTFOUND) Serial.println("FP NO_MATCH");
    else { fail("search", result); return; }
    state = State::Idle;
  } else if (state == State::FirstScan) {
    setState(State::RemoveFinger, "FP ENROLL first_scan_ok remove_finger");
  } else if (state == State::SecondScan) {
    result = sensor.createModel();
    if (result != FINGERPRINT_OK) { fail("combine_scans", result); return; }
    result = sensor.storeModel(enrollmentId);
    if (result != FINGERPRINT_OK) { fail("store", result); return; }
    Serial.printf("FP STORED id=%u\n", enrollmentId);
    state = State::Idle;
  }
}
}
