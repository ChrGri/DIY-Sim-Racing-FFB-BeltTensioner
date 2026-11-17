# ServoPinTest (ControlBoard V7)

Sets the servo pins of the ControlBoard V7 to fixed levels: set the levels at the top of
`src/main.cpp`, flash, switch the servo on and watch its LED.

Open the folder `tools/ServoPinTest` in PlatformIO and Upload (or use the Arduino IDE:
board "ESP32S3 Dev Module", USB CDC On Boot: Enabled). Flash the belt tensioner firmware
again afterwards.

| Pin | GPIO | Note |
| :--- | :--- | :--- |
| TX1 | 2 | RS232 to the servo: HIGH = about -5.5 V at the servo RX (idle), LOW = about +5.5 V |
| RX1 | 3 | driven by the RS232 chip, input by default |
| STEP1 / DIR1 | 4 / 5 | |
| Brake resistor | 1 | always LOW |
