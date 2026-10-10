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
// PCB_VERSION == 9: ControlBoard_PCBA_V2X (LOLIN S3 Mini)
// ------------------------------------------------------------------------------
#elif PCB_VERSION == 9
  #define BOARD_NAME "ControlBoard_PCBA_V2X"

  // Actuator 1 (Primary / Left Belt)
  #define ACTUATOR1_STEP_PIN            38
  #define ACTUATOR1_DIR_PIN             37
  #define ACTUATOR1_ENA_PIN             -1
  #define ACTUATOR1_INVERT_DIR          false
  #define ACTUATOR1_MODBUS_RX_PIN       9
  #define ACTUATOR1_MODBUS_TX_PIN       10
  #define ACTUATOR1_MODBUS_SLAVE_ID     63
  #define ACTUATOR1_ALM_PIN             -1
  #define ACTUATOR1_BRAKE_RESISTOR_PIN  4

  // Actuator 2 (Secondary / Right Belt - Expansion / Header pins)
  #define ACTUATOR2_STEP_PIN            18
  #define ACTUATOR2_DIR_PIN             17
  #define ACTUATOR2_ENA_PIN             -1
  #define ACTUATOR2_INVERT_DIR          false
  #define ACTUATOR2_MODBUS_RX_PIN       15
  #define ACTUATOR2_MODBUS_TX_PIN       16
  #define ACTUATOR2_MODBUS_SLAVE_ID     63
  #define ACTUATOR2_ALM_PIN             -1
  #define ACTUATOR2_BRAKE_RESISTOR_PIN  -1

  // Hardware servo power control (MOSFET switch on GPIO 3)
  #define SERVO_POWER_PIN               3

  // On-board WS2812 RGB LED (GPIO 12 on PCBA V2)
  #ifndef RGB_LED_PIN
    #define RGB_LED_PIN                 12
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
// PCB_VERSION == 6: ControlBoard_V4 (ESP32-S3 DevKit)
// ------------------------------------------------------------------------------
#elif PCB_VERSION == 6
  #define BOARD_NAME "ControlBoard_V4"

  // Actuator 1 (servo RS232 level shifter inverts the UART signal)
  #define ACTUATOR1_STEP_PIN            38
  #define ACTUATOR1_DIR_PIN             37
  #define ACTUATOR1_ENA_PIN             -1
  #define ACTUATOR1_INVERT_DIR          false
  #define ACTUATOR1_MODBUS_RX_PIN       9
  #define ACTUATOR1_MODBUS_TX_PIN       10
  #define ACTUATOR1_MODBUS_SLAVE_ID     63
  #define ACTUATOR1_MODBUS_INVERT       true
  #define ACTUATOR1_ALM_PIN             -1
  #define ACTUATOR1_BRAKE_RESISTOR_PIN  -1

  // Actuator 2 (not available on this PCB)
  #define ACTUATOR2_STEP_PIN            -1
  #define ACTUATOR2_DIR_PIN             -1
  #define ACTUATOR2_ENA_PIN             -1
  #define ACTUATOR2_INVERT_DIR          false
  #define ACTUATOR2_MODBUS_RX_PIN       -1
  #define ACTUATOR2_MODBUS_TX_PIN       -1
  #define ACTUATOR2_MODBUS_SLAVE_ID     63
  #define ACTUATOR2_ALM_PIN             -1
  #define ACTUATOR2_BRAKE_RESISTOR_PIN  -1

  // No status LED (GPIO 48 is routed to the MCP4725 SDA on this PCB)
  #ifndef RGB_LED_PIN
    #define RGB_LED_PIN                 -1
  #endif

// ------------------------------------------------------------------------------
// PCB_VERSION == 12: ControlBoard_V5 (ESP32-S3 DevKit)
// ------------------------------------------------------------------------------
#elif PCB_VERSION == 12
  #define BOARD_NAME "ControlBoard_V5"

  // Actuator 1
  #define ACTUATOR1_STEP_PIN            37
  #define ACTUATOR1_DIR_PIN             36
  #define ACTUATOR1_ENA_PIN             -1
  #define ACTUATOR1_INVERT_DIR          false
  #define ACTUATOR1_MODBUS_RX_PIN       1
  #define ACTUATOR1_MODBUS_TX_PIN       2
  #define ACTUATOR1_MODBUS_SLAVE_ID     63
  #define ACTUATOR1_ALM_PIN             -1
  #define ACTUATOR1_BRAKE_RESISTOR_PIN  35

  // Actuator 2 (not available on this PCB)
  #define ACTUATOR2_STEP_PIN            -1
  #define ACTUATOR2_DIR_PIN             -1
  #define ACTUATOR2_ENA_PIN             -1
  #define ACTUATOR2_INVERT_DIR          false
  #define ACTUATOR2_MODBUS_RX_PIN       -1
  #define ACTUATOR2_MODBUS_TX_PIN       -1
  #define ACTUATOR2_MODBUS_SLAVE_ID     63
  #define ACTUATOR2_ALM_PIN             -1
  #define ACTUATOR2_BRAKE_RESISTOR_PIN  -1

  // On-board WS2812 RGB LED (Waveshare ESP32-S3-DevKitC-1 on GPIO 38)
  #ifndef RGB_LED_PIN
    #define RGB_LED_PIN                 38
  #endif

// ------------------------------------------------------------------------------
// PCB_VERSION == 7: ControlBoard_PCBA_V1X (Gilphilbert PCBA, native USB)
// ------------------------------------------------------------------------------
#elif PCB_VERSION == 7
  #define BOARD_NAME "ControlBoard_PCBA_V1X"

  // Actuator 1 (servo RS232 level shifter inverts the UART signal)
  #define ACTUATOR1_STEP_PIN            38
  #define ACTUATOR1_DIR_PIN             37
  #define ACTUATOR1_ENA_PIN             -1
  #define ACTUATOR1_INVERT_DIR          false
  #define ACTUATOR1_MODBUS_RX_PIN       9
  #define ACTUATOR1_MODBUS_TX_PIN       10
  #define ACTUATOR1_MODBUS_SLAVE_ID     63
  #define ACTUATOR1_MODBUS_INVERT       true
  #define ACTUATOR1_ALM_PIN             -1
  #define ACTUATOR1_BRAKE_RESISTOR_PIN  -1

  // Actuator 2 (not available on this PCB)
  #define ACTUATOR2_STEP_PIN            -1
  #define ACTUATOR2_DIR_PIN             -1
  #define ACTUATOR2_ENA_PIN             -1
  #define ACTUATOR2_INVERT_DIR          false
  #define ACTUATOR2_MODBUS_RX_PIN       -1
  #define ACTUATOR2_MODBUS_TX_PIN       -1
  #define ACTUATOR2_MODBUS_SLAVE_ID     63
  #define ACTUATOR2_ALM_PIN             -1
  #define ACTUATOR2_BRAKE_RESISTOR_PIN  -1

  // On-board WS2812 RGB LED (GPIO 12 on PCBA V1)
  #ifndef RGB_LED_PIN
    #define RGB_LED_PIN                 12
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

// Default: non-inverted servo UART signal unless overridden per board above
#ifndef ACTUATOR1_MODBUS_INVERT
  #define ACTUATOR1_MODBUS_INVERT       false
#endif
#ifndef ACTUATOR2_MODBUS_INVERT
  #define ACTUATOR2_MODBUS_INVERT       false
#endif
