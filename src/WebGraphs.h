#pragma once

#include <Arduino.h>
#include "AxisUnit.h"
#include "Config.h"

#define WEBGRAPHS_MAX_AXES 2

// One telemetry snapshot of a single axis
struct WebGraphAxisSample {
    int32_t stepperPos = 0;     // ESP32 pulse generator position (steps)
    int32_t targetPos = -1;     // Last commanded target position (steps), -1 = none
    int32_t servoPos = 0;       // Unwrapped servo encoder position (steps)
    int32_t speedHz = 0;        // Current pulse generator speed (steps/s, signed)
    int16_t posError = 0;       // Servo following error (steps)
    int16_t currentPct = 0;     // Servo current feedback (%)
    int16_t voltage0p1V = 0;    // DC bus voltage (0.1 V)
    uint8_t homingState = 0;    // HomingState enum
    uint8_t flags = 0;          // Bit0 = packet valid, Bit1 = homed, Bit2 = motor powered
};

struct WebGraphSample {
    uint32_t timeMs = 0;
    WebGraphAxisSample axis[WEBGRAPHS_MAX_AXES];
};

// Web interface with live telemetry graphs, started at boot if ENABLE_WEBGRAPHS is set.
// Never writes to Serial on its own, so the SimHub protocol is not disturbed.
class WebGraphs {
public:
    void begin(AxisUnit** axes, uint8_t axisCount);

    // Starts WiFi + web server in a background task (non-blocking)
    void activate();
    // Prints the current WiFi state and URL (serial command WEBGRAPHS)
    void printStatus(Stream* logStream);

    bool isActive() const { return active; }

    // Called from the Modbus telemetry task after each poll cycle
    void recordSample();

    // Used by the HTTP handlers
    uint32_t copySamples(uint32_t sinceSeq, WebGraphSample* out, uint32_t maxCount, uint32_t& lastSeq);
    AxisUnit** getAxes() const { return axisUnits; }
    uint8_t getAxisCount() const { return numAxes; }

private:
    AxisUnit** axisUnits = nullptr;
    uint8_t numAxes = 0;

    volatile bool active = false;
    bool activationFailed = false;
    TaskHandle_t taskHandle = nullptr;

    WebGraphSample* buffer = nullptr;
    uint32_t writeSeq = 0; // Sequence number of the next sample to be written
    portMUX_TYPE bufferMux = portMUX_INITIALIZER_UNLOCKED;

    static void taskEntry(void* param);
    void runTask();
};

extern WebGraphs webGraphs;
