# ServoPinTest (ControlBoard V7)

Standalone test sketch: puts the servo pins of the ControlBoard V7 on fixed levels, so the
iSV57 can be switched on by hand with defined signals and its LED observed. It also watches
the RS232 RX and ALM inputs and can send Modbus probe requests like the firmware.

**Open the folder `tools/ServoPinTest` in PlatformIO**, Upload, then open the Monitor (115200).
Flash the belt tensioner firmware again afterwards.

| Pin | GPIO | Boot level | Note |
| :--- | :--- | :--- | :--- |
| TX1 | 2 | HIGH | RS232 to servo 1. HIGH = about -5.5 V at the servo RX (idle), LOW = about +5.5 V (break) |
| RX1 | 3 | PULLUP (input) | RS232 from servo 1, driven by the SP3232: input only |
| STEP1 / DIR1 | 4 / 5 | LOW | |
| ALM1 | 6 | PULLUP (input) | servo alarm output |
| TX2 / RX2 / STEP2 / DIR2 | 10 / 9 / 11 / 12 | HIGH / PULLUP / LOW / LOW | second actuator |
| Brake resistor | 1 | LOW (fixed) | |

The boot levels can be changed in the `pins[]` table at the top of `src/main.cpp`.

## Commands

| Command | Example |
| :--- | :--- |
| `<pin> <mode>` (HIGH, LOW, FLOAT, PULLUP, PULLDOWN) | `TX1 LOW`, `STEP1 HIGH`, `DIR1 FLOAT`, `RX1 PULLDOWN` |
| `PROBE <axis> <ms>` / `PROBE <axis> OFF` | `PROBE 1 250`: Modbus request to slave 63 every 250 ms, prints every received byte |
| `MONITOR ON` / `OFF` | prints every level change and edge on RX / ALM (default on) |
| `STATUS`, `HELP` | |

## Test plan for the red servo LED

Switch the servo off, set the levels, switch the servo on (through the inrush current limiter
as usual) and note the LED, about 10 times per row:

1. `TX1 HIGH` (idle level, like the firmware without requests)
2. `TX1 LOW` (permanent +5.5 V at the servo RX)
3. `TX1 HIGH`, `PROBE 1 250` (requests every 250 ms, like the start-up search)
4. `PROBE 1 10` (requests every 10 ms, like the old firmware)
5. Optional: STEP1 / DIR1 HIGH instead of LOW

The monitor also shows whether the servo sends something on RX1 or toggles ALM1 while it boots.
