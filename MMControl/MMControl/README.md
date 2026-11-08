# Mission Marshal control panel

## Component selection

Only one peripheral is active at a time. The panel starts idle. USB commands
remain available in every mode; inactive components are never polled. The
fingerprint startup health check is retained for the D2 ready indication.
Selection does not electrically power peripherals off.

| Command | Result |
| --- | --- |
| `ACTIVATE KEYPAD` | Begin reporting keypad presses. |
| `ACTIVATE FP` | Verify the sensor and select fingerprint commands. |
| `ACTIVATE JOYSTICK` | Initialise ADC inputs and stream raw X/Y values. |
| `ACTIVATE ULTRASONIC` | Initialise TRIG/ECHO and stream distance in centimetres. |
| `DEACTIVATE` | Alias of `RELEASE` / `DISABLE`. |
| `RELEASE` or `DISABLE` | Cancel active work, stop polling, return to idle. |
| `STATUS` | Report `PANEL STATUS active=NONE`, `FP`, or `KEYPAD`. |
| `HELP` | List selection commands. |

Activating while another component is selected returns `release_first`.
Fingerprint commands are rejected unless FP is selected. Finishing a scan or
enrollment keeps FP selected until release. `FP CANCEL` only cancels the current
fingerprint operation; it does not release the component. Arbitrary echo commands
are accepted only while idle. Commands are case-sensitive.

## Ultrasonic wiring and commands

Assumes an HC-SR04-compatible trigger/echo sensor: TRIG -> **GPIO33**,
ECHO -> **GPIO32**, GND -> common GND. Check your model's supply voltage.
Standard 5V HC-SR04 ECHO must pass through a divider or level shifter:
`ECHO -- 1k resistor -- GPIO32 -- 2k resistor -- GND`.
Do not connect a 5V ECHO signal directly to an ESP32 pin.

Send `ACTIVATE ULTRASONIC`; results stream automatically:
`ULTRASONIC DISTANCE cm=12.34 echo_us=716`.
`ULTRASONIC STATUS` reports configuration while selected.
`RELEASE`, `DISABLE` or `DEACTIVATE` stops future measurements
and releases both pins. Each activation restores its output/input configuration.

Measurements deliberately block using `pulseIn(ECHO, HIGH, timeout)`.
Each loop generates a 2 us LOW / 10 us HIGH trigger pulse, waits for the echo,
and immediately prints distance or `echo_timeout` (default timeout: 30 ms).
There are no interrupts, cooldown intervals or additional reporting delays.
Serial commands resume after each measurement returns; release cannot interrupt
a measurement already in progress. Pins/timeout live in PanelConfig.h.
Immediate retriggering can cause acoustic interference: HC-SR04 datasheets
recommend >60 ms between triggers, which this requested sampling mode omits.
Distance uses 343 m/s sound speed without temperature compensation.

GPIO32/33 overlap keypad C1/C2. Selection prevents simultaneous software use;
physical shared connections still need isolation, especially the sensor's ECHO
output and keypad switches. Unplug conflicting wires or use a multiplexer.

## Joystick wiring and commands

VRx -> GPIO4, VRy -> GPIO13, VCC -> **3.3V**, GND -> common GND.
The SW/button pin is unused. Send `ACTIVATE JOYSTICK` to begin automatic reports:
`JOYSTICK POSITION x=2048 y=2048`. Values are raw 12-bit ADC counts (0..4095),
not voltages or calibrated directions; physical centre varies by joystick.
Four samples per axis are averaged; reports arrive every 50 ms even at rest.
`JOYSTICK STATUS` reports configuration while selected. `RELEASE`, `DISABLE`,
or `DEACTIVATE` stops sampling and restores the pins to plain inputs.
Edit JoystickX/Y, JoystickSamples and JoystickReportIntervalMs in PanelConfig.h.
GPIO4/13 are ADC2 inputs; Wi-Fi must remain off while reading them on ESP32.

### Shared-pin lifecycle

Every activation reapplies that module's pin modes and hardware settings:
keypad restores pull-ups/debounce state, joystick restores ADC resolution and
attenuation, fingerprint recreates UART routing and verifies the sensor.
Release tears down the selected module before another can claim its pins.
Fingerprint release detaches UART2 and restores its data pins to INPUT; the
startup health check does the same after setting the D2 last-known-ready indicator.
Do not call standalone diagnostic hardware functions outside this lifecycle.

**Software ownership does not electrically disconnect peripheral wiring.**
GPIO4 is currently shared by joystick X and keypad R1. A physically connected
joystick potentiometer can load a keypad row, and keypad presses can affect
joystick readings. Unplug the conflicting signal, use separate pins, or add a
hardware multiplexer/switch. Future shared outputs also require proper isolation.

## Keypad wiring and coordinates

For a passive 4x4 matrix keypad (no extra library required):

| Keypad wire | ESP32 GPIO |
| --- | --- |
| R1 | 4 |
| R2 | 16 |
| R3 | 18 |
| R4 | 19 |
| C1 | 32 |
| C2 | 33 |
| C3 | 23 |
| C4 | 27 |

GPIO21 (SDA) and GPIO22 (SCL) are reserved for future I2C peripherals.
Use your keypad's labelled row/column order; ribbon connector order varies.
No separate VCC/GND connection is needed for a passive switch matrix.
Coordinates are **one-based**, using the wiring order above: R1/C1 is
`KEYPAD PRESS row=1 col=1`. Each debounced press reports once, then requires
all keys to be released before another report. Multiple simultaneous keys are
rejected to avoid ambiguous readings on a keypad without diodes. A key already
held during activation must be released first. Release leaves keypad pins as
inputs without pull-ups and stops scanning.

### Diagnosing missing rows or false multiple presses

While selected, send `KEYPAD DEBUG ON` to print each debounced matrix change,
including detected coordinates: `KEYPAD RAW mask=0x0001 r1c1`. Zero means all
keys released. `KEYPAD DEBUG OFF` stops this extra logging. `KEYPAD STATUS`
prints an immediate raw snapshot, last debounced state and configured GPIOs.
The first stable state after activation can take about 35 ms to arrive.
Multiple-key errors also include a `KEYPAD DETECTED` coordinate list.

Only the current row drives LOW; inactive rows use weak pull-ups. A 50 us
settling interval is applied both before reading and after releasing each row
(`KeypadSettleUs` in PanelConfig.h). This reduces electrical carry-over between
rows. A row that never appears still needs its cable order/contact checked;
software cannot infer a keypad's ribbon pinout. Debug mode resets on release.

Example Unity sequence: `SendCommand("ACTIVATE KEYPAD")`, read press messages,
then `SendCommand("RELEASE")`. For fingerprint recognition, send
`ACTIVATE FP`, then `FP READ`, then `RELEASE` when finished.

## Wiring and configuration

Edit `include/PanelConfig.h` for pin assignments, baud rates, password and timings.
R307S pin 1 goes to 5V, pin 2 to common GND, pin 3 (TXD) to ESP32
GPIO25, and pin 4 (RXD) to GPIO26. Pins 5/6 are unused.
The ESP32 routes hardware UART2 to these pins; GPIO16/17 remain free.
GPIO2 (D2) is active HIGH after successful password verification and parameter
retrieval. It starts LOW and returns LOW on a detected communication failure.
This is last-known connection status, not continuous sensor health monitoring.
The default configured password is Adafruit's `0x00000000`; if your R307S uses
`0xFFFFFFFF` or a custom password, change the configuration to match.

## Commands

USB serial is **115200 baud**, with **newline/LF** termination (CRLF also works).
Sensor UART is independently **57600 baud**. Commands are case-sensitive.
Only Unity or the PlatformIO serial monitor can own the USB port at a time.

| Command | Result |
| --- | --- |
| `FP READ` | After `ACTIVATE FP`, wait for a finger; return `FP MATCH id=1 confidence=...` or `FP NO_MATCH`. |
| `FP ENROLL 1` | Scan, remove, then scan the same finger again; return `FP STORED id=1`. |
| `FP STATUS` | Report connection, busy state and capacity. |
| `FP CANCEL` | Cancel an active read/enrollment. |
| `FP CONNECT` | Cancel current work and reconnect/check the sensor. |
| `FP HELP` | List commands. |

Enrollment prompts describe each action. Each step times out after 15 seconds.
Errors start with `FP ERROR`; sensor failures include a stage and hexadecimal
library error code. Send another command to retry after image or matching errors.
After communication errors, check wiring/power and use `FP CONNECT`.
IDs are 1 through reported capacity minus one; slot zero is reserved.
**Enrolling into an existing ID replaces that fingerprint after successful scans.**
Templates are stored in the sensor's flash and survive power loss.

From Unity first call `SendCommand("ACTIVATE FP")`, then
`SendCommand("FP ENROLL 1")` or `SendCommand("FP READ")` on
`Esp32SerialConnection`. Its existing receive loop logs every reply.

## Where to extend it

- `src/main.cpp`: startup and loop wiring only.
- `src/SerialCommands.cpp`: command parsing and routing.
- `src/ComponentController.cpp`: exclusive selection, activation and release.
- `src/KeypadService.cpp`: keypad scanning, debouncing and coordinate reporting.
- `src/JoystickService.cpp`: analog pin initialisation, averaged X/Y reporting.
- `src/UltrasonicService.cpp`: trigger pulse and blocking pulseIn echo measurement.
- `src/FingerprintService.cpp`: sensor operations and enrollment state machine.
- `include/PanelConfig.h`: hardware configuration.

The command router starts fingerprint operations only while FP is selected.
Keep calling `ComponentController::update()` in the loop; it dispatches updates
only to the selected peripheral. Waiting for a finger does not block the
loop, so cancel/status commands still work. Individual Adafruit sensor calls are
synchronous and may wait for the library's UART timeout if hardware disconnects.
