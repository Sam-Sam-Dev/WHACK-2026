#pragma once
#include <Arduino.h>

// Owns the sensor UART and workflow. Call update() every loop; no waiting loops
// are used for a player's finger. Individual Adafruit UART calls are synchronous.
namespace FingerprintService {
bool begin();
void deactivate(); // Detach UART and release data pins for other modules.
void update();
void readFinger();
void storeFinger(uint16_t id);
void cancel();
void printStatus();
}
