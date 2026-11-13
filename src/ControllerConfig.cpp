#include "ControllerConfig.h"
#include <Preferences.h>

#define CONTROLLER_CONFIG_NVS_NAMESPACE "ctrlcfg"

enum SettingId : uint8_t {
    SETTING_INVERT_TENSION,
    SETTING_IDLE_POSITION,
    SETTING_MIN_LIMIT,
    SETTING_MAX_LIMIT,
    SETTING_CENTER_AFTER_HOMING,
    SETTING_IDLE_DELAY,
    SETTING_HOME_AT_BOOT,
    SETTING_HOMING_THRESHOLD,
    SETTING_HOMING_BACKOFF,
    SETTING_WEBGRAPHS,
    SETTING_COUNT
};

struct SettingDef {
    const char* key;      // serial name
    const char* nvsKey;   // NVS key (max. 15 characters)
    bool isBool;
    float minValue;
    float maxValue;
    const char* unit;     // "" for none
};

// The limits keep the safety window at least 10 % wide (MIN <= 45, MAX >= 55)
static const SettingDef settingDefs[SETTING_COUNT] = {
    {"INVERT_TENSION",      "invTension",  true,  0.0f,   1.0f,   ""},
    {"IDLE_POSITION",       "idlePct",     false, 0.0f,   50.0f,  "%"},
    {"MIN_LIMIT",           "minPct",      false, 0.0f,   45.0f,  "%"},
    {"MAX_LIMIT",           "maxPct",      false, 55.0f,  100.0f, "%"},
    {"CENTER_AFTER_HOMING", "centerHome",  true,  0.0f,   1.0f,   ""},
    {"IDLE_DELAY",          "idleDelayS",  false, 1.0f,   600.0f, "s"},
    {"HOME_AT_BOOT",        "homeAtBoot",  true,  0.0f,   1.0f,   ""},
    {"HOMING_THRESHOLD",    "homeThresh",  false, 5.0f,   100.0f, "%"},
    {"HOMING_BACKOFF",      "homeBackoff", false, 0.5f,   20.0f,  "mm"},
    {"WEBGRAPHS",           "webGraphs",   true,  0.0f,   1.0f,   ""},
};

static ControllerSettings activeSettings;

static float getValue(const ControllerSettings& s, uint8_t id) {
    switch (id) {
        case SETTING_INVERT_TENSION:      return s.invertTension ? 1.0f : 0.0f;
        case SETTING_IDLE_POSITION:       return s.idlePositionPct;
        case SETTING_MIN_LIMIT:           return s.minSafetyPct;
        case SETTING_MAX_LIMIT:           return s.maxSafetyPct;
        case SETTING_CENTER_AFTER_HOMING: return s.centerAfterHoming ? 1.0f : 0.0f;
        case SETTING_IDLE_DELAY:          return (float)s.idleDelayMs / 1000.0f;
        case SETTING_HOME_AT_BOOT:        return s.homeAtBoot ? 1.0f : 0.0f;
        case SETTING_HOMING_THRESHOLD:    return (float)s.homingThresholdPct;
        case SETTING_HOMING_BACKOFF:      return s.homingBackoffMm;
        case SETTING_WEBGRAPHS:           return s.webGraphs ? 1.0f : 0.0f;
        default:                          return 0.0f;
    }
}

static void setValue(ControllerSettings& s, uint8_t id, float v) {
    switch (id) {
        case SETTING_INVERT_TENSION:      s.invertTension = (v >= 0.5f); break;
        case SETTING_IDLE_POSITION:       s.idlePositionPct = v; break;
        case SETTING_MIN_LIMIT:           s.minSafetyPct = v; break;
        case SETTING_MAX_LIMIT:           s.maxSafetyPct = v; break;
        case SETTING_CENTER_AFTER_HOMING: s.centerAfterHoming = (v >= 0.5f); break;
        case SETTING_IDLE_DELAY:          s.idleDelayMs = (uint32_t)(v * 1000.0f + 0.5f); break;
        case SETTING_HOME_AT_BOOT:        s.homeAtBoot = (v >= 0.5f); break;
        case SETTING_HOMING_THRESHOLD:    s.homingThresholdPct = (uint8_t)(v + 0.5f); break;
        case SETTING_HOMING_BACKOFF:      s.homingBackoffMm = v; break;
        case SETTING_WEBGRAPHS:           s.webGraphs = (v >= 0.5f); break;
        default: break;
    }
}

static bool isValid(uint8_t id, float v) {
    const SettingDef& def = settingDefs[id];
    if (isnan(v) || v < def.minValue || v > def.maxValue) return false;
    if (def.isBool && v != 0.0f && v != 1.0f) return false;
    return true;
}

// Reads the settings; savedMask gets bit <id> set for every value found in NVS
static ControllerSettings loadWithMask(uint16_t& savedMask) {
    ControllerSettings s;
    savedMask = 0;
    Preferences prefs;
    if (prefs.begin(CONTROLLER_CONFIG_NVS_NAMESPACE, true)) {
        for (uint8_t id = 0; id < SETTING_COUNT; id++) {
            const char* key = settingDefs[id].nvsKey;
            if (!prefs.isKey(key)) continue;
            float v = prefs.getFloat(key, NAN);
            if (isValid(id, v)) {
                setValue(s, id, v);
                savedMask |= (1u << id);
            }
        }
        prefs.end();
    }
    return s;
}

namespace ControllerConfig {

const ControllerSettings& begin() {
    uint16_t mask;
    activeSettings = loadWithMask(mask);
    return activeSettings;
}

const ControllerSettings& active() {
    return activeSettings;
}

ControllerSettings load() {
    uint16_t mask;
    return loadWithMask(mask);
}

static void printNumber(Stream* stream, float v, bool isBool) {
    if (isBool) {
        stream->print(v >= 0.5f ? 1 : 0);
    } else {
        stream->print(v, 2);
    }
}

void print(Stream* stream) {
    uint16_t savedMask;
    ControllerSettings saved = loadWithMask(savedMask);
    for (uint8_t id = 0; id < SETTING_COUNT; id++) {
        const SettingDef& def = settingDefs[id];
        float value = getValue(saved, id);
        float activeValue = getValue(activeSettings, id);
        stream->print(F("Setting "));
        stream->print(def.key);
        stream->print(F(" = "));
        printNumber(stream, value, def.isBool);
        if (def.unit[0] != '\0') {
            stream->print(' ');
            stream->print(def.unit);
        }
        stream->print((savedMask & (1u << id)) ? F(" (saved, ") : F(" (default, "));
        printNumber(stream, def.minValue, def.isBool);
        stream->print(F(".."));
        printNumber(stream, def.maxValue, def.isBool);
        stream->print(')');
        if (fabsf(value - activeValue) > 0.001f) {
            stream->print(F(" -> restart to apply"));
        }
        stream->println();
    }
    stream->println(F("End of settings"));
}

void set(Stream* stream, const char* args) {
    char buf[32];
    strlcpy(buf, args, sizeof(buf));
    char* key = buf;
    while (*key == ' ') key++;
    char* valueText = strchr(key, ' ');
    if (valueText == nullptr) {
        stream->println(F("Usage: SET <KEY> <value>   e.g. SET IDLE_POSITION 15 (see SETTINGS)"));
        return;
    }
    *valueText++ = '\0';
    for (char* p = valueText; *p != '\0'; p++) {
        if (*p == ',') *p = '.';
    }

    int8_t found = -1;
    for (uint8_t id = 0; id < SETTING_COUNT; id++) {
        if (strcasecmp(key, settingDefs[id].key) == 0) {
            found = (int8_t)id;
            break;
        }
    }
    if (found < 0) {
        stream->print(F("Invalid setting "));
        stream->print(key);
        stream->println(F(" (see SETTINGS)"));
        return;
    }
    const SettingDef& def = settingDefs[found];

    float value;
    if (strcasecmp(valueText, "ON") == 0 || strcasecmp(valueText, "TRUE") == 0) {
        value = 1.0f;
    } else if (strcasecmp(valueText, "OFF") == 0 || strcasecmp(valueText, "FALSE") == 0) {
        value = 0.0f;
    } else {
        char* end = nullptr;
        value = strtof(valueText, &end);
        while (end != nullptr && *end == ' ') end++;
        if (end == valueText || end == nullptr || *end != '\0') value = NAN;
    }

    if (!isValid((uint8_t)found, value)) {
        stream->print(F("Invalid value for "));
        stream->print(def.key);
        stream->print(F(", allowed: "));
        printNumber(stream, def.minValue, def.isBool);
        stream->print(F(" - "));
        printNumber(stream, def.maxValue, def.isBool);
        if (def.unit[0] != '\0') {
            stream->print(' ');
            stream->print(def.unit);
        }
        stream->println();
        return;
    }

    Preferences prefs;
    bool ok = prefs.begin(CONTROLLER_CONFIG_NVS_NAMESPACE, false) &&
              prefs.putFloat(def.nvsKey, value) == sizeof(float);
    prefs.end();
    if (!ok) {
        stream->println(F("Error: could not write to flash"));
        return;
    }

    stream->print(def.key);
    stream->print(F(" = "));
    printNumber(stream, value, def.isBool);
    stream->println(F(" saved. Restart the controller to apply."));
}

bool saveWebGraphs(bool enabled) {
    Preferences prefs;
    bool ok = prefs.begin(CONTROLLER_CONFIG_NVS_NAMESPACE, false) &&
              prefs.putFloat(settingDefs[SETTING_WEBGRAPHS].nvsKey, enabled ? 1.0f : 0.0f) == sizeof(float);
    prefs.end();
    return ok;
}

void reset() {
    Preferences prefs;
    if (prefs.begin(CONTROLLER_CONFIG_NVS_NAMESPACE, false)) {
        prefs.clear();
        prefs.end();
    }
}

} // namespace ControllerConfig
