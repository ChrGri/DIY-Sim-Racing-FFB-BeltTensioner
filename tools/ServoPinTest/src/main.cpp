// ServoPinTest - ControlBoard V7 (ESP32-S3-Zero)
// Sets the servo pins to fixed levels. Set the levels here, flash, switch the servo on
// and watch its LED. Flash the belt tensioner firmware again afterwards.
#include <Arduino.h>

// ControlBoard V7: set the levels here (HIGH or LOW), then flash
const int TX1_LEVEL   = HIGH;  // GPIO 2: RS232 TX to the servo (HIGH = about -5.5 V at the servo RX, LOW = about +5.5 V)
const int STEP1_LEVEL = LOW;   // GPIO 4
const int DIR1_LEVEL  = LOW;   // GPIO 5

// RX1 (GPIO 3) is driven by the RS232 chip. true = set it as output (drives against the chip!),
// false = leave it an input
const bool RX1_AS_OUTPUT = false;
const int  RX1_LEVEL     = HIGH;

void setup() {
  pinMode(1, OUTPUT);  digitalWrite(1, LOW);            // brake resistor off
  pinMode(2, OUTPUT);  digitalWrite(2, TX1_LEVEL);
  pinMode(4, OUTPUT);  digitalWrite(4, STEP1_LEVEL);
  pinMode(5, OUTPUT);  digitalWrite(5, DIR1_LEVEL);
  if (RX1_AS_OUTPUT) { pinMode(3, OUTPUT); digitalWrite(3, RX1_LEVEL); }
  else               { pinMode(3, INPUT); }
}

void loop() {}
