#pragma once

#include <Arduino.h>
#include "Config.h"

// Behaviour settings of the controller (all axes), changeable via serial and
// stored in NVS (flash). Values that were never set fall back to Config.h.
// Like spindle pitch / working range they are applied after a restart.
//
//   SETTINGS            - list all settings
//   SET <KEY> <value>   - save a setting, e.g. SET IDLE_POSITION 15
//   RESET_CONFIG        - delete all saved values (also pitch / range)
//
// Output format of SETTINGS (parsed by the SimHub plugin, keep it stable):
//   Setting <KEY> = <value>[ <unit>] (<saved|default>, <min>..<max>)[ -> restart to apply]
//   End of settings
struct ControllerSettings {
    bool invertTension = INVERT_TENSION_DIRECTION;
    float idlePositionPct = IDLE_POSITION_PCT;
    float minSafetyPct = MIN_SAFETY_LIMIT_PCT;
    float maxSafetyPct = MAX_SAFETY_LIMIT_PCT;
    bool centerAfterHoming = CENTER_AFTER_CALIBRATION;
    uint32_t idleDelayMs = IDLE_DELAY_MS;
    bool homeAtBoot = CALIBRATE_AT_BOOT;
    uint8_t homingThresholdPct = HOMING_CURRENT_THRESHOLD_PCT;
    float homingBackoffMm = HOMING_BACKOFF_MM;
};

namespace ControllerConfig {
    // Loads the saved settings and remembers them as the active ones (call once at boot)
    const ControllerSettings& begin();
    // Settings the controller runs with (loaded at boot)
    const ControllerSettings& active();
    // Reads the saved settings from NVS (may differ from active() until restart)
    ControllerSettings load();
    // Handles "SET <KEY> <value>" arguments ("<KEY> <value>"), prints the result
    void set(Stream* stream, const char* args);
    // Prints all settings in the format above
    void print(Stream* stream);
    // Deletes all saved settings
    void reset();
}
