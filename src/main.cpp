#include <Arduino.h>
#include <FastAccelStepper.h>
#include "Config.h"
#include "AxisUnit.h"
#include "BeltTensionerProtocol.h"
#include "StatusLed.h"

// Stepper engine instance (hardware RMT / MCPWM pulse generator)
FastAccelStepperEngine engine = FastAccelStepperEngine();

// Actuator units
AxisUnit axisUnit1;
#if (NUM_ACTUATORS >= 2)
AxisUnit axisUnit2;
#endif

// Array of active axis units
AxisUnit* axisUnits[] = {
    &axisUnit1
#if (NUM_ACTUATORS >= 2)
    , &axisUnit2
#endif
};
const uint8_t axisCount = sizeof(axisUnits) / sizeof(AxisUnit*);

// SimHub protocol handler
BeltTensionerProtocol protocol;

// Status RGB LED (Waveshare ESP32-S3 WS2812)
StatusLed statusLed;

// FreeRTOS Task handle for background Modbus telemetry polling
TaskHandle_t taskModbusTelemetryHandle = nullptr;

// Background task running on Core 0 to continuously poll iSV57 servo telemetry
void modbusTelemetryTask(void* pvParameters) {
    while (true) {
        for (uint8_t i = 0; i < axisCount; i++) {
            if (axisUnits[i] != nullptr) {
                axisUnits[i]->pollModbusTelemetry();
            }
        }
        vTaskDelay(pdMS_TO_TICKS(MODBUS_POLL_INTERVAL_MS));
    }
}

void setup() {
    // 0. Enable hardware servo power rail if supported by board
#ifdef SERVO_POWER_PIN
    if (SERVO_POWER_PIN >= 0) {
        pinMode(SERVO_POWER_PIN, OUTPUT);
        digitalWrite(SERVO_POWER_PIN, HIGH);
    }
#endif

    // 1. Immediately clamp all control & communication pins to prevent floating state / glitches
    if (ACTUATOR1_MODBUS_TX_PIN >= 0) {
        pinMode(ACTUATOR1_MODBUS_TX_PIN, OUTPUT);
        digitalWrite(ACTUATOR1_MODBUS_TX_PIN, HIGH);
    }
    if (ACTUATOR1_STEP_PIN >= 0) {
        pinMode(ACTUATOR1_STEP_PIN, OUTPUT);
        digitalWrite(ACTUATOR1_STEP_PIN, LOW);
    }
    if (ACTUATOR1_DIR_PIN >= 0) {
        pinMode(ACTUATOR1_DIR_PIN, OUTPUT);
        digitalWrite(ACTUATOR1_DIR_PIN, LOW);
    }
    if (ACTUATOR1_ENA_PIN >= 0) {
        pinMode(ACTUATOR1_ENA_PIN, OUTPUT);
        digitalWrite(ACTUATOR1_ENA_PIN, LOW);
    }
#if (NUM_ACTUATORS >= 2)
    if (ACTUATOR2_MODBUS_TX_PIN >= 0) {
        pinMode(ACTUATOR2_MODBUS_TX_PIN, OUTPUT);
        digitalWrite(ACTUATOR2_MODBUS_TX_PIN, HIGH);
    }
    if (ACTUATOR2_STEP_PIN >= 0) {
        pinMode(ACTUATOR2_STEP_PIN, OUTPUT);
        digitalWrite(ACTUATOR2_STEP_PIN, LOW);
    }
    if (ACTUATOR2_DIR_PIN >= 0) {
        pinMode(ACTUATOR2_DIR_PIN, OUTPUT);
        digitalWrite(ACTUATOR2_DIR_PIN, LOW);
    }
    if (ACTUATOR2_ENA_PIN >= 0) {
        pinMode(ACTUATOR2_ENA_PIN, OUTPUT);
        digitalWrite(ACTUATOR2_ENA_PIN, LOW);
    }
#endif

    // 2. Initialize Host PC Serial for SimHub communication & serial monitor logging
    Serial.setRxBufferSize(128);
    Serial.begin(SIMHUB_SERIAL_BAUDRATE);
    protocol.begin(axisUnits, axisCount);

    // SimHub BeltTensioner boot greeting
    protocol.sendGreeting(&Serial);

    // 3. Initialize FastAccelStepper pulse generator engine
    engine.init();

    // 4. Configure and initialize Actuator 1
    AxisUnitSettings settings1;
    settings1.stepperId               = 1;
    settings1.stepPin                 = ACTUATOR1_STEP_PIN;
    settings1.dirPin                  = ACTUATOR1_DIR_PIN;
    settings1.enaPin                  = ACTUATOR1_ENA_PIN;
    settings1.almPin                  = ACTUATOR1_ALM_PIN;
    settings1.brakeResistorPin        = ACTUATOR1_BRAKE_RESISTOR_PIN;
    settings1.invertDir               = ACTUATOR1_INVERT_DIR;
    settings1.invertTensionDir        = INVERT_TENSION_DIRECTION;
    settings1.totalWorkingRange       = TOTAL_WORKING_RANGE_STEPS;
    settings1.maxSpeed                = DEFAULT_MAX_SPEED_HZ;
    settings1.acceleration            = DEFAULT_ACCELERATION;
    settings1.homingCurrentThreshold  = HOMING_CURRENT_THRESHOLD_PCT;
    settings1.homingSpeed             = HOMING_SPEED_STEPS_PER_S;
    settings1.homingAccel             = HOMING_ACCELERATION;
    settings1.centerAfterCalib        = CENTER_AFTER_CALIBRATION;
    settings1.homingBackoffSteps      = HOMING_BACKOFF_STEPS;
    settings1.measureMaxTravel        = MEASURE_MAX_TRAVEL_SENSORLESS;

    axisUnit1.begin(&engine,
                    settings1,
                    &Serial2,
                    ACTUATOR1_MODBUS_RX_PIN,
                    ACTUATOR1_MODBUS_TX_PIN,
                    ACTUATOR1_MODBUS_SLAVE_ID,
                    CALIBRATE_AT_BOOT);

    // 5. Configure and initialize Actuator 2 (if enabled)
#if (NUM_ACTUATORS >= 2)
    AxisUnitSettings settings2;
    settings2.stepperId               = 2;
    settings2.stepPin                 = ACTUATOR2_STEP_PIN;
    settings2.dirPin                  = ACTUATOR2_DIR_PIN;
    settings2.enaPin                  = ACTUATOR2_ENA_PIN;
    settings2.almPin                  = ACTUATOR2_ALM_PIN;
    settings2.brakeResistorPin        = ACTUATOR2_BRAKE_RESISTOR_PIN;
    settings2.invertDir               = ACTUATOR2_INVERT_DIR;
    settings2.invertTensionDir        = INVERT_TENSION_DIRECTION;
    settings2.totalWorkingRange       = TOTAL_WORKING_RANGE_STEPS;
    settings2.maxSpeed                = DEFAULT_MAX_SPEED_HZ;
    settings2.acceleration            = DEFAULT_ACCELERATION;
    settings2.homingCurrentThreshold  = HOMING_CURRENT_THRESHOLD_PCT;
    settings2.homingSpeed             = HOMING_SPEED_STEPS_PER_S;
    settings2.homingAccel             = HOMING_ACCELERATION;
    settings2.centerAfterCalib        = CENTER_AFTER_CALIBRATION;
    settings2.homingBackoffSteps      = HOMING_BACKOFF_STEPS;
    settings2.measureMaxTravel        = MEASURE_MAX_TRAVEL_SENSORLESS;

    axisUnit2.begin(&engine,
                    settings2,
                    &Serial1,
                    ACTUATOR2_MODBUS_RX_PIN,
                    ACTUATOR2_MODBUS_TX_PIN,
                    ACTUATOR2_MODBUS_SLAVE_ID,
                    CALIBRATE_AT_BOOT);
#endif

    // 6. Launch FreeRTOS background task on Core 0 for Modbus telemetry & stall monitoring
    xTaskCreatePinnedToCore(
        modbusTelemetryTask,
        "modbusTask",
        4096,
        nullptr,
        1,
        &taskModbusTelemetryHandle,
        TASK_CORE_MODBUS_TELEMETRY
    );

    // 7. Initialize on-board RGB Status LED (WS2812)
    statusLed.begin(RGB_LED_GPIO, RGB_LED_BRIGHTNESS);
}

void loop() {
    // 1. Process incoming SimHub commands on USB serial
    protocol.processIncomingStream(&Serial);

    // 2. Update sensorless homing and idle parking watchdogs
    for (uint8_t i = 0; i < axisCount; i++) {
        if (axisUnits[i] != nullptr) {
            axisUnits[i]->updateIdleWatchdog(IDLE_DELAY_MS);
        }
    }

    // 3. Update status RGB LED color based on system state
    statusLed.update(axisUnits, axisCount);

    // 4. Yield CPU to prevent Task Watchdog (TWDT) reset on Core 1
    yield();
}
