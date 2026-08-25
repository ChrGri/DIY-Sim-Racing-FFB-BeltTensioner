#pragma once
#include <Arduino.h>

// ==============================================================================
// BOARD PIN DEFINITIONS BASED ON PCB_VERSION
// ==============================================================================

#ifndef PCB_VERSION
  #define PCB_VERSION 13 // Default to ControlBoard_V6 (ESP32-S3)
#endif

// ------------------------------------------------------------------------------
// PCB_VERSION == 13: ControlBoard_V6 (ESP32-S3 DevKit)
// ------------------------------------------------------------------------------
#if PCB_VERSION == 13
  #define BOARD_NAME "ControlBoard_V6"

  // Actuator 1 (Primary / Left Belt)
  #define ACTUATOR1_STEP_PIN            37
  #define ACTUATOR1_DIR_PIN             36
  #define ACTUATOR1_ENA_PIN             -1
  #define ACTUATOR1_INVERT_DIR          false
  #define ACTUATOR1_MODBUS_RX_PIN       1
  #define ACTUATOR1_MODBUS_TX_PIN       2
  #define ACTUATOR1_MODBUS_SLAVE_ID     63
  #define ACTUATOR1_ALM_PIN             11
  #define ACTUATOR1_BRAKE_RESISTOR_PIN  35

  // Actuator 2 (Secondary / Right Belt - Expansion / Header pins)
  #define ACTUATOR2_STEP_PIN            16
  #define ACTUATOR2_DIR_PIN             17
  #define ACTUATOR2_ENA_PIN             -1
  #define ACTUATOR2_INVERT_DIR          false
  #define ACTUATOR2_MODBUS_RX_PIN       6
  #define ACTUATOR2_MODBUS_TX_PIN       7
  #define ACTUATOR2_MODBUS_SLAVE_ID     63
  #define ACTUATOR2_ALM_PIN             -1
  #define ACTUATOR2_BRAKE_RESISTOR_PIN  -1

  // On-board WS2812 RGB LED (Waveshare ESP32-S3-DevKitC-1 on GPIO 38)
  #ifndef RGB_LED_PIN
    #define RGB_LED_PIN                 38
  #endif

// ------------------------------------------------------------------------------
// PCB_VERSION == 14: ControlBoard_V7 (ESP32-S3-Zero)
// ------------------------------------------------------------------------------
#elif PCB_VERSION == 14
  #define BOARD_NAME "ControlBoard_V7"

  // Actuator 1
  #define ACTUATOR1_STEP_PIN            4
  #define ACTUATOR1_DIR_PIN             5
  #define ACTUATOR1_ENA_PIN             -1
  #define ACTUATOR1_INVERT_DIR          false
  #define ACTUATOR1_MODBUS_RX_PIN       3
  #define ACTUATOR1_MODBUS_TX_PIN       2
  #define ACTUATOR1_MODBUS_SLAVE_ID     63
  #define ACTUATOR1_ALM_PIN             6
  #define ACTUATOR1_BRAKE_RESISTOR_PIN  1

  // Actuator 2
  #define ACTUATOR2_STEP_PIN            11
  #define ACTUATOR2_DIR_PIN             12
  #define ACTUATOR2_ENA_PIN             -1
  #define ACTUATOR2_INVERT_DIR          false
  #define ACTUATOR2_MODBUS_RX_PIN       9
  #define ACTUATOR2_MODBUS_TX_PIN       10
  #define ACTUATOR2_MODBUS_SLAVE_ID     63
  #define ACTUATOR2_ALM_PIN             -1
  #define ACTUATOR2_BRAKE_RESISTOR_PIN  -1

  // On-board WS2812 RGB LED (Waveshare ESP32-S3 Zero)
  #ifndef RGB_LED_PIN
    #define RGB_LED_PIN                 21
  #endif

// ------------------------------------------------------------------------------
// PCB_VERSION == 3: Classic ESP32 DevKit (V3)
// ------------------------------------------------------------------------------
#elif PCB_VERSION == 3
  #define BOARD_NAME "ControlBoard_V3_ESP32"

  // Actuator 1
  #define ACTUATOR1_STEP_PIN            23
  #define ACTUATOR1_DIR_PIN             22
  #define ACTUATOR1_ENA_PIN             -1
  #define ACTUATOR1_INVERT_DIR          false
  #define ACTUATOR1_MODBUS_RX_PIN       26
  #define ACTUATOR1_MODBUS_TX_PIN       27
  #define ACTUATOR1_MODBUS_SLAVE_ID     63
  #define ACTUATOR1_ALM_PIN             -1
  #define ACTUATOR1_BRAKE_RESISTOR_PIN  -1

  // Actuator 2
  #define ACTUATOR2_STEP_PIN            18
  #define ACTUATOR2_DIR_PIN             19
  #define ACTUATOR2_ENA_PIN             -1
  #define ACTUATOR2_INVERT_DIR          false
  #define ACTUATOR2_MODBUS_RX_PIN       16
  #define ACTUATOR2_MODBUS_TX_PIN       17
  #define ACTUATOR2_MODBUS_SLAVE_ID     63
  #define ACTUATOR2_ALM_PIN             -1
  #define ACTUATOR2_BRAKE_RESISTOR_PIN  -1

  #ifndef RGB_LED_PIN
    #define RGB_LED_PIN                 -1
  #endif

// ------------------------------------------------------------------------------
// Generic / Fallback default pins
// ------------------------------------------------------------------------------
#else
  #define BOARD_NAME "Generic_ESP32"

  #define ACTUATOR1_STEP_PIN            18
  #define ACTUATOR1_DIR_PIN             19
  #define ACTUATOR1_ENA_PIN             -1
  #define ACTUATOR1_INVERT_DIR          false
  #define ACTUATOR1_MODBUS_RX_PIN       16
  #define ACTUATOR1_MODBUS_TX_PIN       17
  #define ACTUATOR1_MODBUS_SLAVE_ID     63
  #define ACTUATOR1_ALM_PIN             -1
  #define ACTUATOR1_BRAKE_RESISTOR_PIN  -1

  #define ACTUATOR2_STEP_PIN            22
  #define ACTUATOR2_DIR_PIN             23
  #define ACTUATOR2_ENA_PIN             -1
  #define ACTUATOR2_INVERT_DIR          false
  #define ACTUATOR2_MODBUS_RX_PIN       26
  #define ACTUATOR2_MODBUS_TX_PIN       27
  #define ACTUATOR2_MODBUS_SLAVE_ID     63
  #define ACTUATOR2_ALM_PIN             -1
  #define ACTUATOR2_BRAKE_RESISTOR_PIN  -1

  #ifndef RGB_LED_PIN
    #define RGB_LED_PIN                 21
  #endif
#endif
