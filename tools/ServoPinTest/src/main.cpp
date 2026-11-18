// ServoPinTest - ControlBoard V7 (ESP32-S3-Zero)
// Sets the servo pins to fixed levels, RX1 toggles every second, TX1 every two seconds.
// Set the levels here, flash, switch the servo on and watch its LED.
// Flash the belt tensioner firmware again afterwards.
#include <Arduino.h>

// ControlBoard V7: set the levels here (HIGH or LOW), then flash
const int STEP1_LEVEL = LOW;   // GPIO 4
const int DIR1_LEVEL  = LOW;   // GPIO 5

// TX1 (GPIO 2): RS232 TX to the servo (HIGH = about -5.5 V at the servo RX, LOW = about +5.5 V)
const int  TX1_START_LEVEL = HIGH;
const unsigned long TX1_TOGGLE_MS = 2000;   // 0 = no toggling

// RX1 (GPIO 3) is driven by the RS232 chip. true = output, toggles (drives against the chip!),
// false = leave it an input
const bool RX1_AS_OUTPUT = true;
const int  RX1_START_LEVEL = HIGH;
const unsigned long RX1_TOGGLE_MS = 1000;   // 0 = no toggling

int tx1Level = TX1_START_LEVEL;
int rx1Level = RX1_START_LEVEL;
unsigned long lastTx1ToggleMs = 0;
unsigned long lastRx1ToggleMs = 0;

void setup() {
  pinMode(1, OUTPUT);  digitalWrite(1, LOW);            // brake resistor off
  pinMode(2, OUTPUT);  digitalWrite(2, tx1Level);
  pinMode(4, OUTPUT);  digitalWrite(4, STEP1_LEVEL);
  pinMode(5, OUTPUT);  digitalWrite(5, DIR1_LEVEL);
  if (RX1_AS_OUTPUT) { pinMode(3, OUTPUT); digitalWrite(3, rx1Level); }
  else               { pinMode(3, INPUT); }
  lastTx1ToggleMs = millis();
  lastRx1ToggleMs = lastTx1ToggleMs;
}

void loop() {
  unsigned long now = millis();
  if (TX1_TOGGLE_MS > 0 && now - lastTx1ToggleMs >= TX1_TOGGLE_MS) {
    lastTx1ToggleMs += TX1_TOGGLE_MS;
    tx1Level = (tx1Level == HIGH) ? LOW : HIGH;
    digitalWrite(2, tx1Level);
  }
  if (RX1_AS_OUTPUT && RX1_TOGGLE_MS > 0 && now - lastRx1ToggleMs >= RX1_TOGGLE_MS) {
    lastRx1ToggleMs += RX1_TOGGLE_MS;
    rx1Level = (rx1Level == HIGH) ? LOW : HIGH;
    digitalWrite(3, rx1Level);
  }
}
