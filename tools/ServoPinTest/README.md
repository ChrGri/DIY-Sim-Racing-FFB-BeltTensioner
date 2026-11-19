# ServoPinTest (ControlBoard V7 / V8)

Sets the servo pins to fixed levels (TX1 / RX1 can toggle): choose the board (`#define BOARD`)
and the levels at the top of `src/main.cpp`, flash, switch the servo on and watch its LED.

Open the folder `tools/ServoPinTest` in PlatformIO and Upload (or use the Arduino IDE:
board "ESP32S3 Dev Module", USB CDC On Boot: Enabled). Flash the belt tensioner firmware
again afterwards.

| Function | V7 | V8 |
| :--- | :--- | :--- |
| RS232 TX (MAX3232 T_IN, to the servo RX) | GPIO 2 | GPIO 5 |
| RS232 RX (MAX3232 R_OUT, from the servo TX) | GPIO 3 | GPIO 6 |
| STEP | GPIO 4 | GPIO 4 |
| DIR | GPIO 5 | GPIO 3 |
| ALM | GPIO 6 | GPIO 2 |
| Brake resistor | GPIO 1 (LOW) | - |

TX1 HIGH = about -5.5 V at the servo RX (idle), LOW = about +5.5 V (break).
RX1 is driven by the MAX3232 and is an input by default.
