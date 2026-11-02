#pragma once
#include "BoardPins.h"
#include <Arduino.h>

// ==============================================================================
// 1. GENERAL & ACTUATOR CONFIGURATION
// ==============================================================================

// Set to 1 for single belt tensioner (mono), or 2 for dual belt tensioner (left
// + right)
#define NUM_ACTUATORS 1

// Firmware version reported to SimHub BeltTensioner plugin (SimHub expects
// "2.0")
#define FIRMWARE_VERSION "2.0"

// Serial communication baud rate with SimHub
#define SIMHUB_SERIAL_BAUDRATE 250000

// ==============================================================================
// 2. MECHANICAL & MOTION CONFIGURATION
// ==============================================================================

// Motor & Lead Screw / Linear Rail specifications
// SPINDLE_PITCH_MM and TOTAL_WORKING_RANGE_MM are defaults: they can be set per
// axis via the serial commands SET_PITCH / SET_RANGE (saved in flash, see CONFIG)
#define STEPS_PER_MOTOR_REV                                                    \
  3200U // Microsteps configured in iSV57 (default 3200)
#define SPINDLE_PITCH_MM                                                       \
  10.0f // Lead screw / ball screw pitch in mm/rev (e.g. 5mm or 10mm)
#define TOTAL_WORKING_RANGE_MM                                                 \
  60.0f // Total usable stroke length on linear rail in mm (50mm)

// Calculated working range in steps: (stroke_mm / pitch_mm) * steps_per_rev
#define TOTAL_WORKING_RANGE_STEPS                                              \
  ((int32_t)((TOTAL_WORKING_RANGE_MM / SPINDLE_PITCH_MM) *                     \
             (float)STEPS_PER_MOTOR_REV))

// Speeds and accelerations are given in carriage units (m/s, m/s^2) and
// converted per axis with its own spindle pitch, so axes with different pitches
// move identically.
// Note: FastAccelStepper operates stably up to ~200 kHz on ESP32. 190 kHz provides peak performance without queue throttling.
#define MAX_STEP_RATE_HZ          190000U // Hard limit of the pulse generator per axis (safety clamp)
// Maximum speed and acceleration refer to a 5 mm spindle pitch, the finest
// supported pitch: there they use the full 190 kHz / 6.0M steps/s^2. Coarser
// pitches (e.g. 10 mm) reach the same m/s with fewer steps (95 kHz).
#define DEFAULT_MAX_SPEED_M_S     0.296875f // = 190 kHz at 5 mm pitch (~3,562 RPM)
#define DEFAULT_ACCELERATION_M_S2 9.375f    // = 6.0M steps/s^2 at 5 mm pitch - reaches 0.30 m/s in ~32 ms

// Multipliers for speed (CMD 2) and acceleration (CMD 3) commands from SimHub
// SimHub UI limits: 36,000 speed  -> 0.296875 m/s
//                   100,000 accel -> 9.375 m/s^2
#define SPEED_MULTIPLIER          (DEFAULT_MAX_SPEED_M_S / 36000.0f)       // m/s per SimHub unit
#define ACCELERATION_MULTIPLIER   (DEFAULT_ACCELERATION_M_S2 / 100000.0f)  // m/s^2 per SimHub unit

// Soft-start ramp: 0 = Disabled for instant responsiveness
#define SOFT_START_RAMP_MS        0U

// Inactivity delay before moving to idle/parked position and disabling servo axis (10 seconds)
#define IDLE_DELAY_MS             10000U

// ==============================================================================
// 3. TENSION DIRECTION & SAFETY BOUNDARIES
// ==============================================================================

// Invert tension direction (pull/release direction for braking):
// false = 0% input (idle) is at MIN stop, 100% input (braking) pulls towards
// MAX stop true  = 0% input (idle) is at MAX stop, 100% input (braking) pulls
// towards MIN stop
#define INVERT_TENSION_DIRECTION false

// Software safety margins: Carriage will NEVER travel below 5% or above 95% of
// travel
#define MIN_SAFETY_LIMIT_PCT 5.0f  // 5% minimum position
#define MAX_SAFETY_LIMIT_PCT 95.0f // 95% maximum position

// Position to move to after calibration / idle (10% position)
#define IDLE_POSITION_PCT 10.0f // 10% tension position

// Position after homing/idle: true = centered (50% tension), false = loose (10%
// or 90%)
#define CENTER_AFTER_CALIBRATION false

// Perform sensorless homing automatically at ESP32 boot
#define CALIBRATE_AT_BOOT false

// ==============================================================================
// 4. STEP LOSS RECOVERY (via iSV57 Physical Optical Encoder Telemetry)
// ==============================================================================

// Injects measured optical encoder offsets into the pulse generator at
// standstill
#define ENABLE_STEP_LOSS_RECOVERY true
#define MAX_STEPS_TO_RECOVER_PER_CALL                                          \
  2 // Max step correction per cycle to ensure smooth compensation

// ==============================================================================
// 5. SENSORLESS HOMING SETTINGS (via iSV57 Modbus Current Telemetry)
// ==============================================================================

// Servo load current threshold (in %) to detect physical mechanical stop
#define HOMING_CURRENT_THRESHOLD_PCT 15

// Homing approach speed and acceleration (carriage units, same for every pitch)
#define HOMING_SPEED_M_S 0.015625f        // 15.6 mm/s (= 5000 steps/s at 10 mm pitch)
#define HOMING_ACCELERATION_M_S2 0.078125f // 78 mm/s^2 (= 25000 steps/s^2 at 10 mm pitch)

// Distance to back off from the hard stop after stall detection
#define HOMING_BACKOFF_MM 3.125f // (= 1000 steps at 10 mm pitch)

// Minimum travel before the MAX stall check starts / minimum plausible measured range
#define HOMING_MIN_TRAVEL_MM 6.25f // (= 2000 steps at 10 mm pitch)

// Measure physical maximum travel by driving to both MIN and MAX mechanical
// stops (false = single stop homing)
#define MEASURE_MAX_TRAVEL_SENSORLESS false

// Modbus baud rate for iSV57 servo communication
#define ISV57_MODBUS_BAUDRATE 38400

// ==============================================================================
// 6. FREERTOS TASK CONFIGURATION
// ==============================================================================
#define TASK_CORE_MODBUS_TELEMETRY                                             \
  0 // Core 0 handles cyclic Modbus telemetry & stall monitoring
#define TASK_CORE_MAIN_LOOP                                                    \
  1 // Core 1 handles SimHub packet processing & motion dispatch
#define MODBUS_POLL_INTERVAL_MS                                                \
  10 // Polling cycle time for iSV57 servo state registers

// ==============================================================================
// 7. STATUS RGB LED (Waveshare ESP32-S3 On-Board WS2812 RGB LED)
// ==============================================================================
#define ENABLE_RGB_STATUS_LED true
#define RGB_LED_GPIO RGB_LED_PIN // Defaults to GPIO 21 on Waveshare ESP32-S3
#define RGB_LED_BRIGHTNESS                                                     \
  40 // Brightness: 1 to 255 (40 is comfortable & glare-free)

// ==============================================================================
// 8. WEB GRAPHS (live telemetry plots in the browser)
// ==============================================================================
// true = WiFi starts at boot: the ESP32 opens its own access point (or joins
// the network below) and serves live graphs of position, current and voltage
// on port 80. Starts silently (no serial output, SimHub stays compatible); send
// WEBGRAPHS in the serial monitor to print the address.
// false = WiFi stays completely off and the web graphs are not compiled in.
#define ENABLE_WEBGRAPHS true
#define WEBGRAPHS_AP_SSID "diy_belt_tensioner"
#define WEBGRAPHS_AP_PASSWORD "belttensioner" // min. 8 characters, "" = open network
// Optional: join an existing WiFi network instead of opening an access point.
// The network can also be set on the web page ("WiFi" button); credentials
// entered there are stored in NVS (flash) and take precedence over these
// defaults. Leave the SSID empty to always use the access point. If joining
// fails within WEBGRAPHS_STA_TIMEOUT_MS, the access point is opened as fallback.
#define WEBGRAPHS_STA_SSID ""
#define WEBGRAPHS_STA_PASSWORD ""
#define WEBGRAPHS_STA_TIMEOUT_MS 15000U
// After joining a network from the access point page, the access point stays
// open this long so the page can show the new address
#define WEBGRAPHS_AP_LINGER_MS 60000U
// Number of telemetry samples kept on the ESP32 between two browser requests
#define WEBGRAPHS_BUFFER_SIZE 512
#define TASK_CORE_WEBGRAPHS                                                    \
  0 // Web server runs next to WiFi and Modbus telemetry on Core 0
