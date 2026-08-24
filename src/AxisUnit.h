#pragma once

#include <Arduino.h>
#include <FastAccelStepper.h>
#include "isv57communication.h"
#include "Config.h"

enum HomingState {
    HOMING_IDLE,
    HOMING_START,
    HOMING_WAIT_SERVO,
    HOMING_APPROACH_MIN,
    HOMING_BACKOFF_MIN,
    HOMING_MEASURE_MAX_APPROACH,
    HOMING_MEASURE_MAX_BACKOFF,
    HOMING_RETURN_HOME,
    HOMING_DONE,
    HOMING_FAILED
};

struct AxisUnitSettings {
    uint8_t stepperId = 1;
    int8_t stepPin = -1;
    int8_t dirPin = -1;
    int8_t enaPin = -1;
    int8_t almPin = -1;
    int8_t brakeResistorPin = -1;
    bool invertDir = false;
    bool invertTensionDir = INVERT_TENSION_DIRECTION;
    bool enableStepLossRecov = ENABLE_STEP_LOSS_RECOVERY;
    int32_t totalWorkingRange = TOTAL_WORKING_RANGE_STEPS;
    uint32_t maxSpeed = DEFAULT_MAX_SPEED_HZ;
    uint32_t acceleration = DEFAULT_ACCELERATION;
    uint8_t homingCurrentThreshold = HOMING_CURRENT_THRESHOLD_PCT;
    uint32_t homingSpeed = HOMING_SPEED_STEPS_PER_S;
    uint32_t homingAccel = HOMING_ACCELERATION;
    bool centerAfterCalib = CENTER_AFTER_CALIBRATION;
    int32_t homingBackoffSteps = HOMING_BACKOFF_STEPS;
    bool measureMaxTravel = MEASURE_MAX_TRAVEL_SENSORLESS;
};

struct AxisUnitState {
    bool motorPowered = false;
    bool motorReady = false;
    int32_t targetPosition = -1;
    unsigned long lastActivityTime = 0;
    unsigned long firstActivityTime = 0;
    bool finalVelocityApplied = false;
    HomingState homingState = HOMING_IDLE;
    uint32_t homingStateStartTime = 0;
};

class AxisUnit {
public:
    AxisUnitSettings settings;
    AxisUnitState state;
    Isv57Communication isv57;

    // Step Loss Recovery variables (lockless 32-bit atomic on ESP32-S3)
    volatile int32_t servo_offset_compensation_steps_i32 = 0;
    int32_t servoPosCorrected_i32 = 0;
    volatile bool servoInitialized = false;
    volatile bool isFlashing = false;

    AxisUnit();

    bool begin(FastAccelStepperEngine* engine,
               const AxisUnitSettings& axisSettings,
               HardwareSerial* serial,
               int8_t rxPin,
               int8_t txPin,
               int16_t slaveId,
               bool calibrateAtBoot = true);

    void setPosition16Bits(uint16_t inputPosition);
    void setMaxSpeed16Bits(uint16_t speed);
    void setMaxAcceleration32Bits(uint32_t accel);

    void startSensorlessHoming();
    void updateHoming();
    void updateIdleWatchdog(unsigned long idleTimeoutMs);
    void moveToIdle(bool blocking = false);

    bool enableMotor();
    void disableMotor();
    bool flashTunedParameters(Stream* logStream = nullptr);

    void pollModbusTelemetry();
    void unwrapAndCalculateStepLoss();
    void correctPos();
    void printStatus();

    bool isHomed() const { return state.homingState == HOMING_DONE; }
    bool isMotorReady() const { return state.motorReady; }
    int32_t getCurrentPosition() const;
    int16_t getCurrentLoadPercent() const { return isv57.dynamicStates.servo_current_percent; }
    float getBusVoltage() const { return ((float)isv57.dynamicStates.servoVoltage0p1V_i16) * 0.1f; }
    int32_t getWorkingRange() const { return hardLimitMax; }

    int32_t convertPosition16Bits(uint16_t inputPosition) const {
        int32_t minPos = (int32_t)((MIN_SAFETY_LIMIT_PCT / 100.0f) * (float)hardLimitMax);
        int32_t maxPos = (int32_t)((MAX_SAFETY_LIMIT_PCT / 100.0f) * (float)hardLimitMax);
        int32_t target;
        if (settings.invertTensionDir) {
            // Inverted: 0 input (idle/no brake) -> maxPos (relaxed), 65535 input (full brake) -> minPos (pulled tight)
            target = maxPos - (int32_t)(((uint64_t)inputPosition * (uint64_t)(maxPos - minPos)) / 65535ULL);
        } else {
            // Normal: 0 input (idle/no brake) -> minPos (relaxed), 65535 input (full brake) -> maxPos (pulled tight)
            target = minPos + (int32_t)(((uint64_t)inputPosition * (uint64_t)(maxPos - minPos)) / 65535ULL);
        }
        return constrain(target, minPos, maxPos);
    }

private:
    FastAccelStepper* stepper = nullptr;
    String logHeader;
    int32_t hardLimitMin = 0;
    int32_t hardLimitMax = TOTAL_WORKING_RANGE_STEPS;

    void log(const String& msg);
    void setSpeedLive(uint32_t speed);
};
