// ServoPinTest - ControlBoard V7 / V8 (ESP32-S3-Zero)
// Sets the servo pins to fixed levels, RX1 / TX1 can toggle. Set the board and the levels here,
// flash, switch the servo on and watch its LED. Flash the belt tensioner firmware again afterwards.
#include <Arduino.h>

// ---- Board: 8 = ControlBoard V8, 7 = ControlBoard V7 -------------------------
#define BOARD 7

#if BOARD == 8
const int PIN_TX1 = 5;    // to MAX3232 T_IN  -> servo RX
const int PIN_RX1 = 6;    // from MAX3232 R_OUT <- servo TX
const int PIN_STEP1 = 4;
const int PIN_DIR1 = 3;
const int PIN_BRAKE = -1; // no brake resistor on V8
const int BOARD_RGB_LED_PIN = 21;
#else
const int PIN_TX1 = 2;
const int PIN_RX1 = 3;
const int PIN_STEP1 = 4;
const int PIN_DIR1 = 5;
const int PIN_BRAKE = 1;
const int BOARD_RGB_LED_PIN = 21;
#endif

// ---- Levels -----------------------------------------------------------------
const int STEP1_LEVEL = LOW;
const int DIR1_LEVEL  = LOW;

// TX1: HIGH = about -5.5 V at the servo RX (idle), LOW = about +5.5 V (break)
const int  TX1_START_LEVEL = HIGH;
const unsigned long TX1_TOGGLE_MS = 2000;   // 0 = no toggling

// RX1 is driven by the MAX3232. true = output, toggles (drives against the chip!),
// false = leave it an input
const bool RX1_AS_OUTPUT = true;
const int  RX1_START_LEVEL = HIGH;
const unsigned long RX1_TOGGLE_MS = 1000;   // 0 = no toggling

int tx1Level = TX1_START_LEVEL;
int rx1Level = RX1_START_LEVEL;
unsigned long lastTx1ToggleMs = 0;
unsigned long lastRx1ToggleMs = 0;

// On-board RGB LED blinks every second (sketch runs), its colour shows TX1: green = HIGH, red = LOW
const unsigned long LED_TOGGLE_MS = 1000;
bool ledOn = true;
unsigned long lastLedToggleMs = 0;

void showTx1Level() {
  if (!ledOn)                rgbLedWrite(BOARD_RGB_LED_PIN, 0, 0, 0);
  else if (tx1Level == HIGH) rgbLedWrite(BOARD_RGB_LED_PIN, 0, 30, 0);
  else                       rgbLedWrite(BOARD_RGB_LED_PIN, 30, 0, 0);
}

void setup() {
  if (PIN_BRAKE >= 0) { pinMode(PIN_BRAKE, OUTPUT); digitalWrite(PIN_BRAKE, LOW); } // brake resistor off
  pinMode(PIN_TX1, OUTPUT);   digitalWrite(PIN_TX1, tx1Level);
  pinMode(PIN_STEP1, OUTPUT); digitalWrite(PIN_STEP1, STEP1_LEVEL);
  pinMode(PIN_DIR1, OUTPUT);  digitalWrite(PIN_DIR1, DIR1_LEVEL);
  if (RX1_AS_OUTPUT) { pinMode(PIN_RX1, OUTPUT); digitalWrite(PIN_RX1, rx1Level); }
  else               { pinMode(PIN_RX1, INPUT); }
  lastTx1ToggleMs = millis();
  lastRx1ToggleMs = lastTx1ToggleMs;
  lastLedToggleMs = lastTx1ToggleMs;
  showTx1Level();
  Serial.begin(115200);
}

void loop() {
  unsigned long now = millis();
  if (now - lastLedToggleMs >= LED_TOGGLE_MS) {
    lastLedToggleMs += LED_TOGGLE_MS;
    ledOn = !ledOn;
    showTx1Level();
    Serial.printf("[%lu s] ServoPinTest V%d running: TX1 (GPIO %d) = %d, RX1 (GPIO %d) = %d, STEP1 = %d, DIR1 = %d\n",
                  now / 1000, BOARD, PIN_TX1, tx1Level, PIN_RX1,
                  RX1_AS_OUTPUT ? rx1Level : digitalRead(PIN_RX1), STEP1_LEVEL, DIR1_LEVEL);
  }
  if (TX1_TOGGLE_MS > 0 && now - lastTx1ToggleMs >= TX1_TOGGLE_MS) {
    lastTx1ToggleMs += TX1_TOGGLE_MS;
    tx1Level = (tx1Level == HIGH) ? LOW : HIGH;
    digitalWrite(PIN_TX1, tx1Level);
    showTx1Level();
  }
  if (RX1_AS_OUTPUT && RX1_TOGGLE_MS > 0 && now - lastRx1ToggleMs >= RX1_TOGGLE_MS) {
    lastRx1ToggleMs += RX1_TOGGLE_MS;
    rx1Level = (rx1Level == HIGH) ? LOW : HIGH;
    digitalWrite(PIN_RX1, rx1Level);
  }
}
