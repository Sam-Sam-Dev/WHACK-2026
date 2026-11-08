#pragma once

// This is the only module deciding which peripheral receives update() calls.
// Add future peripherals here without coupling their implementations together.
namespace ComponentController {
enum class Component { None, Fingerprint, Keypad, Joystick, Ultrasonic };
Component active();
void activate(Component component);
void release();
void update();
void printStatus();
}
