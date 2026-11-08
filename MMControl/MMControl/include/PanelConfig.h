#pragma once
#include <Arduino.h>

// Change wiring and timing here rather than inside the fingerprint workflow.
namespace PanelConfig {
constexpr uint32_t UsbBaud = 115200;
constexpr uint32_t FingerprintBaud = 57600;
// UART2 is routed to these GPIOs by the ESP32 hardware; no bit-banging needed.
constexpr int FingerprintRx = 25; // ESP32 RX <- sensor pin 3 (TXD)
constexpr int FingerprintTx = 26; // ESP32 TX -> sensor pin 4 (RXD)
constexpr int SensorReadyLed = 2; // D2 is assumed to mean GPIO2, active HIGH.
constexpr uint32_t FingerprintPassword = 0x00000000;
constexpr uint32_t StepTimeoutMs = 15000;
constexpr uint32_t PollIntervalMs = 100;
// Passive 4x4 matrix keypad: wire its labelled R1..R4 and C1..C4 in this order.
constexpr int KeypadRows[4] = {16, 17, 18, 19};
// Keep GPIO21 (SDA) and GPIO22 (SCL) free for I2C peripherals.
constexpr int KeypadColumns[4] = {32, 33, 23, 27};
constexpr uint32_t KeypadScanIntervalMs = 5;
constexpr uint32_t KeypadDebounceMs = 30;
// Allow weak internal pull-ups and keypad cable capacitance to settle per row.
constexpr uint32_t KeypadSettleUs = 50;
constexpr int JoystickX = 25;
constexpr int JoystickY = 27;
constexpr uint32_t JoystickReportIntervalMs = 50; // 20 reports/second
constexpr int JoystickSamples = 4; // Average a few samples to reduce ADC noise.
constexpr int UltrasonicTrigger = 33;
constexpr int UltrasonicEcho = 32;
constexpr uint32_t UltrasonicEchoTimeoutUs = 30000;
}
