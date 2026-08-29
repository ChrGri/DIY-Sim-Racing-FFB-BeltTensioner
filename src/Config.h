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
#define STEPS_PER_MOTOR_REV                                                    \
  3200U // Microsteps configured in iSV57 (default 3200)
#define SPINDLE_PITCH_MM                                                       \
  10.0f // Lead screw / ball screw pitch in mm/rev (e.g. 5mm or 10mm)
#define TOTAL_WORKING_RANGE_MM                                                 \
  50.0f // Total usable stroke length on linear rail in mm (50mm)

// Calculated working range in steps: (stroke_mm / pitch_mm) * steps_per_rev
#define TOTAL_WORKING_RANGE_STEPS                                              \
  ((int32_t)((TOTAL_WORKING_RANGE_MM / SPINDLE_PITCH_MM) *                     \
             (float)STEPS_PER_MOTOR_REV))

// High-performance speeds and accelerations for snappy FFB belt tensioning
#define DEFAULT_MAX_SPEED_HZ                                                   \
  190000U // 245 kHz (~4,593 RPM = ~765 mm/s) - maximum high-speed pulse rate
#define DEFAULT_ACCELERATION                                                   \
  6000000U // 6.0M steps/s^2 (~1,875 rev/s^2) - crisp, instantaneous torque
           // delivery

// Multipliers for speed (CMD 2) and acceleration (CMD 3) commands from SimHub
// SimHub UI limits: 36,000 Hz speed -> 36,000 * 6.80556f = 245,000 Hz (245 kHz)
//                   100,000 accel   -> 100,000 * 60.0f    = 6,000,000 steps/s^2
#define SPEED_MULTIPLIER ((float)DEFAULT_MAX_SPEED_HZ / (float)36000.0f)
#define ACCELERATION_MULTIPLIER 60.0f

// Soft-start ramp: 0 = Disabled for instant responsiveness
#define SOFT_START_RAMP_MS 0U

// Inactivity delay before moving to idle/parked position and disabling servo
// axis (10 seconds)
#define IDLE_DELAY_MS 10000U

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

// Homing approach speed in steps per second
#define HOMING_SPEED_STEPS_PER_S 5000U
#define HOMING_ACCELERATION 25000U

// Number of steps to back off from the hard stop after stall detection
#define HOMING_BACKOFF_STEPS 1000

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
