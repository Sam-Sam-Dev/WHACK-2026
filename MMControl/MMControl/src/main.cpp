/*
 * COMMAND QUICK REFERENCE (USB Serial, 115200 baud, newline terminated)
 *   ACTIVATE KEYPAD  -> report KEYPAD PRESS row=1..4 col=1..4
 *   ACTIVATE JOYSTICK -> initialise GPIO4/X and GPIO13/Y; report 0..4095 values
 *   ACTIVATE ULTRASONIC -> TRIG GPIO33 / ECHO GPIO32; stream distance in cm
 *   ULTRASONIC STATUS -> report ultrasonic pin/timing settings while selected
 *   DEACTIVATE       -> alias of RELEASE; stops and releases selected module
 *   ACTIVATE FP      -> select fingerprint sensor; then use FP commands below
 *   FP READ          -> identify a saved finger
 *   FP ENROLL <id>   -> save a finger after two scans (replaces an existing ID)
 *   FP STATUS / FP CANCEL / FP CONNECT / FP HELP -> require FP selected
 *   RELEASE / DISABLE -> stop current component and return to idle
 *   STATUS / HELP     -> panel selection / command list, available in every mode
 *
 * FILE ROLE: entry point only; keep peripheral logic in its own service.
 * setup(): open laptop serial, check the fingerprint sensor for the D2 ready
 * indicator, and report idle. The health check does not activate fingerprint mode.
 * loop(): process laptop commands first, then update ONLY the selected component.
 * Finishing an operation does not release its component; send RELEASE explicitly.
 *
 * EDITING MAP
 *   PanelConfig.h          -> pins, baud rates, sensor password, timing
 *   SerialCommands.cpp     -> command text, validation and routing
 *   ComponentController.cpp -> exclusive activation, release and update dispatch
 *   FingerprintService.cpp / KeypadService.cpp -> device-specific behavior
 * USB UART0 remains separate from sensor UART2 on GPIO25/26.
 * All selections stop polling, not electrical power. See README.md for wiring.
 */
#include <Arduino.h>
#include "PanelConfig.h"
#include "FingerprintService.h"
#include "SerialCommands.h"
#include "ComponentController.h"

int rows[4] = {4, 16, 18, 19};
int cols[4] = {32, 33, 23, 27};

void KeyPadSetup()
{
  for (int i = 0; i < 4; i++)
  {
    pinMode(rows[i], INPUT_PULLUP);
    pinMode(cols[i], INPUT_PULLUP);
  }
}

void KeyPadRead()
{
  for(int i = 0; i < 4; i++)
  {
    pinMode(rows[i], OUTPUT);
    digitalWrite(rows[i], LOW);
    for (int j = 0; j < 4; j++)
    {
      if (digitalRead(cols[j]) == LOW)
      {
        Serial.printf("KEYPAD PRESS row=%d col=%d\n", i + 1, j + 1);
      }
    }
    digitalWrite(rows[i], HIGH);
    pinMode(rows[i], INPUT_PULLUP);
  }
}

void setup() {
  
  Serial.begin(PanelConfig::UsbBaud);
  FingerprintService::begin(); // Startup health check / D2 indication only.
  FingerprintService::deactivate(); // Release UART pins while the panel is idle.
  ComponentController::printStatus(); // No component starts active.
  //Serial.begin(115200);
  //KeyPadSetup();

}

void loop() {
  listenForSerialCommands();
  ComponentController::update();
  //KeyPadRead();
}

