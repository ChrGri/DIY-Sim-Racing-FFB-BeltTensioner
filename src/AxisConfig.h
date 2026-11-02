#pragma once

#include <Arduino.h>
#include "Config.h"

#define AXIS_CONFIG_MAX_AXES 2

// Plausibility limits for values set via serial
#define AXIS_CONFIG_MIN_PITCH_MM 0.5f
#define AXIS_CONFIG_MAX_PITCH_MM 50.0f
#define AXIS_CONFIG_MIN_RANGE_MM 5.0f
#define AXIS_CONFIG_MAX_RANGE_MM 500.0f

// Mechanical configuration of one axis.
// Stored per axis in NVS (flash) via the serial commands SET_PITCH / SET_RANGE and
// read at boot; values that were never set fall back to SPINDLE_PITCH_MM /
// TOTAL_WORKING_RANGE_MM from Config.h.
struct AxisMechConfig {
    float spindlePitchMm = SPINDLE_PITCH_MM;
    float workingRangeMm = TOTAL_WORKING_RANGE_MM;
    bool pitchSaved = false; // false = Config.h default
    bool rangeSaved = false; // false = Config.h default

    float stepsPerMm() const { return (float)STEPS_PER_MOTOR_REV / spindlePitchMm; }
    int32_t workingRangeSteps() const { return (int32_t)(workingRangeMm * stepsPerMm()); }
};

namespace AxisConfig {
    // axisIndex is 0-based (0 = M1, 1 = M2)
    AxisMechConfig load(uint8_t axisIndex);
    bool savePitch(uint8_t axisIndex, float pitchMm);
    bool saveRange(uint8_t axisIndex, float rangeMm);
    // Deletes all saved values (Config.h defaults apply after the next restart)
    void reset();
}
