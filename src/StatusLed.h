#pragma once
#include <Arduino.h>
#include "Config.h"
#include "BoardPins.h"
#include "AxisUnit.h"

enum LedColorState {
    LED_OFF,
    LED_RED,
    LED_GREEN,
    LED_BLUE,
    LED_YELLOW,
    LED_CYAN,
    LED_MAGENTA,
    LED_WHITE
};

class StatusLed {
private:
    int8_t ledPin;
    uint8_t brightness;
    uint32_t lastBlinkTime = 0;
    bool blinkState = false;

    void writeRgb(uint8_t r, uint8_t g, uint8_t b) {
        if (ledPin < 0) return;
        // Scale with configured brightness (0-255)
        uint8_t rScaled = (uint8_t)(((uint16_t)r * (uint16_t)brightness) / 255);
        uint8_t gScaled = (uint8_t)(((uint16_t)g * (uint16_t)brightness) / 255);
        uint8_t bScaled = (uint8_t)(((uint16_t)b * (uint16_t)brightness) / 255);

        #if defined(RGB_BUILTIN) || defined(SOC_RMT_SUPPORTED) || defined(ESP32)
            // On-board WS2812 RGB LED expects GRB byte ordering
            rgbLedWrite(ledPin, gScaled, rScaled, bScaled);
        #endif
    }

public:
    StatusLed() : ledPin(RGB_LED_GPIO), brightness(RGB_LED_BRIGHTNESS) {}

    void begin(int8_t pin = RGB_LED_GPIO, uint8_t bright = RGB_LED_BRIGHTNESS) {
        ledPin = pin;
        brightness = bright;
        if (ledPin >= 0) {
            pinMode(ledPin, OUTPUT);
            // Start with Cyan during boot
            setColor(LED_CYAN);
        }
    }

    void setBrightness(uint8_t b) {
        brightness = b;
    }

    void setColor(LedColorState color) {
        switch (color) {
            case LED_OFF:     writeRgb(0, 0, 0); break;
            case LED_RED:     writeRgb(255, 0, 0); break;
            case LED_GREEN:   writeRgb(0, 255, 0); break;
            case LED_BLUE:    writeRgb(0, 0, 255); break;
            case LED_YELLOW:  writeRgb(255, 180, 0); break;
            case LED_CYAN:    writeRgb(0, 255, 255); break;
            case LED_MAGENTA: writeRgb(255, 0, 255); break;
            case LED_WHITE:   writeRgb(255, 255, 255); break;
        }
    }

    void update(AxisUnit** axisUnits, uint8_t axisCount) {
        if (!ENABLE_RGB_STATUS_LED || ledPin < 0 || axisUnits == nullptr || axisCount == 0) return;

        uint32_t now = millis();
        bool anyFlashing = false;
        bool anyError = false;
        bool anyHoming = false;
        bool anyPowered = false;
        bool allHomed = true;
        bool anyParking = false;

        for (uint8_t i = 0; i < axisCount; i++) {
            if (axisUnits[i] == nullptr) continue;

            if (axisUnits[i]->isFlashing) {
                anyFlashing = true;
            }
            if (axisUnits[i]->state.homingState == HOMING_FAILED) {
                anyError = true;
            }
            if (axisUnits[i]->state.homingState != HOMING_DONE && axisUnits[i]->state.homingState != HOMING_IDLE) {
                anyHoming = true;
            }
            if (axisUnits[i]->state.motorPowered) {
                anyPowered = true;
            }
            if (axisUnits[i]->state.homingState != HOMING_DONE) {
                allHomed = false;
            }
            if (axisUnits[i]->state.isParked) {
                anyParking = true;
            }
        }

        // 1. EEPROM parameter flash in progress -> Magenta blink
        if (anyFlashing) {
            if (now - lastBlinkTime > 150) {
                lastBlinkTime = now;
                blinkState = !blinkState;
                setColor(blinkState ? LED_MAGENTA : LED_OFF);
            }
            return;
        }

        // 2. Hardware / Homing Error -> Fast Red blink
        if (anyError) {
            if (now - lastBlinkTime > 200) {
                lastBlinkTime = now;
                blinkState = !blinkState;
                setColor(blinkState ? LED_RED : LED_OFF);
            }
            return;
        }

        // 3. Homing / Calibration in progress -> Blue pulsing / blink
        if (anyHoming) {
            if (now - lastBlinkTime > 300) {
                lastBlinkTime = now;
                blinkState = !blinkState;
                setColor(blinkState ? LED_BLUE : LED_CYAN);
            }
            return;
        }

        // 4. Moving to idle park position before standby -> Yellow
        if (anyParking) {
            setColor(LED_YELLOW);
            return;
        }

        // 5. Normal Active Drive Mode (Powered and Homed) -> Solid Green
        if (anyPowered && allHomed) {
            setColor(LED_GREEN);
            return;
        }

        // 6. Inactive / Standby / Motor Off -> Solid Red
        if (!anyPowered) {
            setColor(LED_RED);
            return;
        }

        // 7. Boot / Idle unhomed
        setColor(LED_CYAN);
    }
};
