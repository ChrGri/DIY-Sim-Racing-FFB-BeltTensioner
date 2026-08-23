#include "AxisUnit.h"

AxisUnit::AxisUnit() {
    state.motorPowered = true;
    state.motorReady = false;
    state.targetPosition = -1;
    state.lastActivityTime = 0;
    state.firstActivityTime = 0;
    state.finalVelocityApplied = false;
    state.homingState = HOMING_IDLE;
    hardLimitMin = 0;
    hardLimitMax = TOTAL_WORKING_RANGE_STEPS;
    servo_offset_compensation_steps_i32 = 0;
    servoPosCorrected_i32 = 0;
}

void AxisUnit::log(const String& msg) {
    Serial.print(logHeader);
    Serial.println(msg);
}

bool AxisUnit::begin(FastAccelStepperEngine* engine,
                     const AxisUnitSettings& axisSettings,
                     HardwareSerial* serial,
                     int8_t rxPin,
                     int8_t txPin,
                     int16_t slaveId,
                     bool calibrateAtBoot) {
    settings = axisSettings;
    logHeader = "M" + String(settings.stepperId) + " ";
    hardLimitMax = settings.totalWorkingRange;

    if (settings.brakeResistorPin >= 0) {
        pinMode(settings.brakeResistorPin, OUTPUT);
        digitalWrite(settings.brakeResistorPin, LOW);
    }

    if (txPin >= 0) {
        pinMode(txPin, OUTPUT);
        digitalWrite(txPin, HIGH);
    }

    // 1. Initialize Modbus communication with iSV57 servo
    isv57.setSerial(serial, slaveId);
    isv57.initialize(ISV57_MODBUS_BAUDRATE, rxPin, txPin);

    // Initial servo discovery, configure telemetry
    if (isv57.findServosSlaveId()) {
        isv57.clearServoAlarms();
        isv57.setupServoStateReading();
        isv57.sendTunedServoParameters(settings.invertDir, STEPS_PER_MOTOR_REV);
        delay(30);
        isv57.readServoStates();
    }

    state.motorPowered = true;
    state.motorReady = false;
    state.lastActivityTime = millis();

    // 2. Initialize FastAccelStepper pulse generator
    if (engine != nullptr && settings.stepPin >= 0) {
        stepper = engine->stepperConnectToPin(settings.stepPin);
        if (stepper) {
            stepper->setDirectionPin(settings.dirPin, settings.invertDir);
            stepper->setAutoEnable(true);
            stepper->enableOutputs();
            stepper->setSpeedInHz(settings.maxSpeed);
            stepper->setAcceleration(settings.acceleration);
            stepper->applySpeedAcceleration();
            stepper->setCurrentPosition(0);
        } else {
            return false;
        }
    }

    if (calibrateAtBoot) {
        startSensorlessHoming();
    } else {
        state.homingState = HOMING_IDLE;
        state.motorReady = true;
    }

    return true;
}

void AxisUnit::pollModbusTelemetry() {
    isv57.readServoStates();

    // If telemetry packet is valid and axis is homed, calculate step loss offset
    if (isv57.dynamicStates.servo_receivedPacketIsValid_b && state.homingState == HOMING_DONE) {
        unwrapAndCalculateStepLoss();
    }
}

void AxisUnit::unwrapAndCalculateStepLoss() {
    int32_t rawServoPos = (int32_t)isv57.getPosFromMin();

    // Invert reading if motor direction is not inverted
    if (!settings.invertDir) {
        rawServoPos *= -1;
    }

    int32_t espPos = (stepper != nullptr) ? stepper->getCurrentPosition() : 0;
    int32_t posDiff = espPos - rawServoPos;

    // Handle 16-bit encoder wrap-arounds (overflow / underflow)
    int32_t wraps = 0;
    if (posDiff > 32767) {
        wraps = (posDiff + 32768) / 65536;
    } else if (posDiff < -32768) {
        wraps = (posDiff - 32767) / 65536;
    }

    servoPosCorrected_i32 = rawServoPos + (wraps * 65536);

    if (settings.enableStepLossRecov) {
        // Step loss offset = ESP's step count - Servo's true optical encoder position
        int32_t servo_offset_compensation_steps_local = espPos - servoPosCorrected_i32;

        // Lockless 32-bit atomic assignment on ESP32-S3
        servo_offset_compensation_steps_i32 = servo_offset_compensation_steps_local;
    }
}

void AxisUnit::correctPos() {
    if (stepper == nullptr || stepper->isRunning()) return;

    if (servo_offset_compensation_steps_i32 != 0) {
        int32_t stepOffset = (int32_t)constrain(servo_offset_compensation_steps_i32,
            -(int32_t)MAX_STEPS_TO_RECOVER_PER_CALL,
            (int32_t)MAX_STEPS_TO_RECOVER_PER_CALL);

        stepper->setCurrentPosition(stepper->getCurrentPosition() - stepOffset);
        servo_offset_compensation_steps_i32 = 0; // Prevent overcompensation
    }
}

bool AxisUnit::enableMotor() {
    if (!state.motorPowered) {
        if (stepper != nullptr) {
            stepper->enableOutputs();
        }
        state.motorPowered = true;
        state.targetPosition = -1;
        state.lastActivityTime = millis();
        log(F("Enabling motor"));
    }

    if (state.homingState != HOMING_DONE && state.homingState != HOMING_IDLE) {
        updateHoming();
        return false;
    }

    state.motorReady = true;
    return true;
}

void AxisUnit::disableMotor() {
    if (state.motorPowered) {
        if (stepper != nullptr) {
            stepper->disableOutputs();
        }
        state.motorPowered = false;
        state.targetPosition = -1;
        log(F("Disabling motor"));
    }
    state.firstActivityTime = 0;
}

void AxisUnit::startSensorlessHoming() {
    log(F("Starting stepper calibration"));
    state.homingState = HOMING_START;
    state.motorReady = false;
    state.homingStateStartTime = millis();
}

void AxisUnit::updateHoming() {
    if (stepper == nullptr) return;

    switch (state.homingState) {
        case HOMING_IDLE:
        case HOMING_DONE:
        case HOMING_FAILED:
            break;

        case HOMING_START:
            stepper->enableOutputs();
            stepper->setSpeedInHz(settings.homingSpeed);
            stepper->setAcceleration(settings.homingAccel);
            stepper->applySpeedAcceleration();
            state.homingState = HOMING_WAIT_SERVO;
            state.homingStateStartTime = millis();
            break;

        case HOMING_WAIT_SERVO:
            if (millis() - state.homingStateStartTime > 300) {
                // Step 1: Move backward to find mechanical MIN hard stop
                stepper->runBackward();
                state.homingState = HOMING_APPROACH_MIN;
                state.homingStateStartTime = millis();
                log(F("Starting calibration"));
            }
            break;

        case HOMING_APPROACH_MIN: {
            int16_t currentLoad = abs(isv57.dynamicStates.servo_current_percent);
            bool stallDetected = (currentLoad >= settings.homingCurrentThreshold);

            // Timeout failsafe (25s)
            if (millis() - state.homingStateStartTime > 25000) {
                stepper->forceStop();
                state.homingState = HOMING_FAILED;
                state.motorReady = true;
                break;
            }

            if (stallDetected) {
                stepper->forceStop();
                stepper->setCurrentPosition(-settings.homingBackoffSteps);
                stepper->moveTo(0);
                state.homingState = HOMING_BACKOFF_MIN;
            }
            break;
        }

        case HOMING_BACKOFF_MIN:
            if (!stepper->isRunning()) {
                // Set home zero position
                stepper->setCurrentPosition(0);
                isv57.setZeroPos();
                hardLimitMin = 0;

                if (settings.measureMaxTravel) {
                    // Step 2: Move forward to find mechanical MAX hard stop
                    stepper->runForward();
                    state.homingState = HOMING_MEASURE_MAX_APPROACH;
                    state.homingStateStartTime = millis();
                } else {
                    // Single-endstop homing: assume configured working range
                    hardLimitMax = settings.totalWorkingRange;
                    float idlePct = settings.invertTensionDir ? (100.0f - IDLE_POSITION_PCT) : IDLE_POSITION_PCT;
                    int32_t targetIdle = (int32_t)((idlePct / 100.0f) * (float)hardLimitMax);
                    stepper->setSpeedInHz(settings.homingSpeed);
                    stepper->setAcceleration(settings.homingAccel);
                    stepper->applySpeedAcceleration();
                    stepper->moveTo(targetIdle);
                    state.homingState = HOMING_RETURN_HOME;
                }
            }
            break;

        case HOMING_MEASURE_MAX_APPROACH: {
            int16_t currentLoad = abs(isv57.dynamicStates.servo_current_percent);
            int32_t stepPos = stepper->getCurrentPosition();
            // Ignore stall check for first 2000 steps to clear the MIN backoff region
            bool stallDetected = (stepPos > 2000) && (currentLoad >= settings.homingCurrentThreshold);

            // Timeout failsafe (25s)
            if (millis() - state.homingStateStartTime > 25000) {
                stepper->forceStop();
                hardLimitMax = settings.totalWorkingRange;
                state.homingState = HOMING_MEASURE_MAX_BACKOFF;
                break;
            }

            if (stallDetected) {
                stepper->forceStop();
                int32_t measured = stepper->getCurrentPosition();
                hardLimitMax = measured - settings.homingBackoffSteps;
                if (hardLimitMax < 2000) {
                    hardLimitMax = settings.totalWorkingRange;
                }
                settings.totalWorkingRange = hardLimitMax;

                // Back off from MAX hard stop
                stepper->moveTo(hardLimitMax);
                state.homingState = HOMING_MEASURE_MAX_BACKOFF;
            }
            break;
        }

        case HOMING_MEASURE_MAX_BACKOFF:
            if (!stepper->isRunning()) {
                // Step 3: Move to relaxed park position
                float idlePct = settings.invertTensionDir ? (100.0f - IDLE_POSITION_PCT) : IDLE_POSITION_PCT;
                int32_t targetIdle = (int32_t)((idlePct / 100.0f) * (float)hardLimitMax);
                stepper->setSpeedInHz(settings.homingSpeed);
                stepper->setAcceleration(settings.homingAccel);
                stepper->applySpeedAcceleration();
                stepper->moveTo(targetIdle);
                state.homingState = HOMING_RETURN_HOME;
            }
            break;

        case HOMING_RETURN_HOME:
            if (!stepper->isRunning()) {
                // Homing completely finished! Restore normal high speed & snappy acceleration
                stepper->setSpeedInHz(settings.maxSpeed);
                stepper->setAcceleration(settings.acceleration);
                stepper->applySpeedAcceleration();
                state.homingState = HOMING_DONE;
                state.motorReady = true;
                log(F("Calibration successful."));
            }
            break;
    }
}

void AxisUnit::setPosition16Bits(uint16_t inputPosition) {
    int32_t newPosition = convertPosition16Bits(inputPosition);

    if (enableMotor()) {
        state.lastActivityTime = millis();

        if (!state.finalVelocityApplied) {
            stepper->setSpeedInHz(settings.maxSpeed);
            stepper->setAcceleration(settings.acceleration);
            stepper->applySpeedAcceleration();
            state.finalVelocityApplied = true;
        }

        if (newPosition != state.targetPosition) {
            state.targetPosition = newPosition;
            stepper->moveTo(newPosition);
        }
    }
}

void AxisUnit::setMaxSpeed16Bits(uint16_t speed) {
    if (speed > 0) {
        settings.maxSpeed = (uint32_t)((float)speed * SPEED_MULTIPLIER);
        if (stepper != nullptr && state.motorReady) {
            stepper->setSpeedInHz(settings.maxSpeed);
            stepper->applySpeedAcceleration();
        }
        state.finalVelocityApplied = false;
        log("Speed set to " + String(settings.maxSpeed));
    }
}

void AxisUnit::setMaxAcceleration32Bits(uint32_t accel) {
    if (accel > 0) {
        settings.acceleration = (uint32_t)((float)accel * ACCELERATION_MULTIPLIER);
        if (stepper != nullptr && state.motorReady) {
            stepper->setAcceleration(settings.acceleration);
            stepper->applySpeedAcceleration();
        }
        state.finalVelocityApplied = false;
        log("Acceleration set to " + String(settings.acceleration));
    }
}

void AxisUnit::moveToIdle(bool blocking) {
    if (stepper == nullptr) return;

    stepper->setSpeedInHz(settings.homingSpeed);
    stepper->setAcceleration(settings.homingAccel);
    stepper->applySpeedAcceleration();

    // Park at relaxed position (10% or 90% depending on invertTensionDir)
    float idlePct = settings.invertTensionDir ? (100.0f - IDLE_POSITION_PCT) : IDLE_POSITION_PCT;
    int32_t idlePos = (int32_t)((idlePct / 100.0f) * (float)hardLimitMax);
    stepper->moveTo(idlePos, blocking);
}

void AxisUnit::updateIdleWatchdog(unsigned long idleTimeoutMs) {
    // 1. Perform continuous step-loss position correction when at standstill
    correctPos();

    if (state.homingState != HOMING_DONE && state.homingState != HOMING_IDLE) {
        updateHoming();
        return;
    }

    if (state.motorPowered) {
        if (millis() - state.lastActivityTime > idleTimeoutMs) {
            if (state.firstActivityTime != 0) {
                state.firstActivityTime = 0;
                moveToIdle(false);
            }
        }
    }
}

int32_t AxisUnit::getCurrentPosition() const {
    return (stepper != nullptr) ? stepper->getCurrentPosition() : 0;
}
