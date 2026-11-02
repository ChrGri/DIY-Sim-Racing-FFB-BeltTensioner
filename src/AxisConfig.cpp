#include "AxisConfig.h"
#include <Preferences.h>

#define AXIS_CONFIG_NVS_NAMESPACE "axiscfg"

// NVS keys per axis: "pitch1", "range1", "pitch2", ...
static String nvsKey(const char* name, uint8_t axisIndex) {
    return String(name) + String(axisIndex + 1);
}

namespace AxisConfig {

AxisMechConfig load(uint8_t axisIndex) {
    AxisMechConfig config;
    Preferences prefs;
    if (axisIndex < AXIS_CONFIG_MAX_AXES && prefs.begin(AXIS_CONFIG_NVS_NAMESPACE, true)) {
        String pitchKey = nvsKey("pitch", axisIndex);
        String rangeKey = nvsKey("range", axisIndex);
        if (prefs.isKey(pitchKey.c_str())) {
            float pitch = prefs.getFloat(pitchKey.c_str(), SPINDLE_PITCH_MM);
            if (pitch >= AXIS_CONFIG_MIN_PITCH_MM && pitch <= AXIS_CONFIG_MAX_PITCH_MM) {
                config.spindlePitchMm = pitch;
                config.pitchSaved = true;
            }
        }
        if (prefs.isKey(rangeKey.c_str())) {
            float range = prefs.getFloat(rangeKey.c_str(), TOTAL_WORKING_RANGE_MM);
            if (range >= AXIS_CONFIG_MIN_RANGE_MM && range <= AXIS_CONFIG_MAX_RANGE_MM) {
                config.workingRangeMm = range;
                config.rangeSaved = true;
            }
        }
        prefs.end();
    }
    return config;
}

static bool saveFloat(const char* name, uint8_t axisIndex, float value) {
    if (axisIndex >= AXIS_CONFIG_MAX_AXES) return false;
    Preferences prefs;
    if (!prefs.begin(AXIS_CONFIG_NVS_NAMESPACE, false)) return false;
    bool ok = (prefs.putFloat(nvsKey(name, axisIndex).c_str(), value) == sizeof(float));
    prefs.end();
    return ok;
}

bool savePitch(uint8_t axisIndex, float pitchMm) {
    if (pitchMm < AXIS_CONFIG_MIN_PITCH_MM || pitchMm > AXIS_CONFIG_MAX_PITCH_MM) return false;
    return saveFloat("pitch", axisIndex, pitchMm);
}

bool saveRange(uint8_t axisIndex, float rangeMm) {
    if (rangeMm < AXIS_CONFIG_MIN_RANGE_MM || rangeMm > AXIS_CONFIG_MAX_RANGE_MM) return false;
    return saveFloat("range", axisIndex, rangeMm);
}

void reset() {
    Preferences prefs;
    if (prefs.begin(AXIS_CONFIG_NVS_NAMESPACE, false)) {
        prefs.clear();
        prefs.end();
    }
}

} // namespace AxisConfig
