/*
 * ServoPinTest - ControlBoard V7 (ESP32-S3-Zero)
 *
 * Standalone test: sets the servo pins (RS232 TX, STEP, DIR) to fixed levels and watches
 * the inputs (RS232 RX, ALM), so the iSV57 can be switched on by hand with defined signals
 * and its LED observed. Optionally sends Modbus probe requests like the firmware does.
 *
 * What the servo sees on its RS232 RX is set by the ESP TX pin (inverted by the SP3232):
 *   TX HIGH  -> about -5.5 V at the servo RX (idle level, as in normal operation)
 *   TX LOW   -> about +5.5 V at the servo RX (permanent "space" / break)
 *   0 V at the servo RX is only possible with the cable unplugged.
 * The ESP RX pin is driven by the SP3232 receiver: it can only be an input here
 * (FLOAT / PULLUP / PULLDOWN). Driving it would fight the transceiver.
 *
 * Commands (USB serial monitor, 115200, end with Enter, not case sensitive):
 *   STATUS                    all pins, levels and edge counters
 *   <pin> <mode>              e.g. TX1 HIGH, STEP1 LOW, DIR1 FLOAT, RX1 PULLUP
 *                             pins:  TX1 RX1 STEP1 DIR1 ALM1  TX2 RX2 STEP2 DIR2
 *                             modes: HIGH LOW FLOAT PULLUP PULLDOWN (inputs: FLOAT PULLUP PULLDOWN)
 *   PROBE <1|2> <ms>          send a Modbus request (slave 63, read 2 registers) every <ms>
 *                             on axis 1/2 and print every received byte
 *   PROBE <1|2> OFF           stop, TX/RX get their previous modes back
 *   MONITOR ON|OFF            print every level change of the inputs (default ON)
 *   HELP
 *
 * Typical test: TX1 HIGH (default), switch the servo on, watch the LED and the RX1 edges.
 * Then TX1 LOW, and PROBE 1 250 for the request hypothesis.
 */
#include <Arduino.h>

// ----------------------------------------------------------------------------
// ControlBoard V7 pins (src/BoardPins.h, PCB_VERSION 14)
// ----------------------------------------------------------------------------
static const int PIN_BRAKE_RESISTOR_1 = 1; // always LOW (brake resistor off)
static const uint32_t MODBUS_BAUD = 38400;
static const uint8_t MODBUS_SLAVE_ID = 63;

enum TestMode { M_LOW, M_HIGH, M_FLOAT, M_PULLUP, M_PULLDOWN, M_UART };
static const char* MODE_NAMES[] = {"LOW", "HIGH", "FLOAT", "PULLUP", "PULLDOWN", "UART"};

struct TestPin {
    const char* name;
    int gpio;
    bool inputOnly;   // driven by the board (RS232 receiver, servo ALM output)
    TestMode mode;     // mode at boot: change here to have it from the first millisecond
    // runtime
    volatile uint32_t edges;
    uint32_t reportedEdges;
    int lastLevel;
};

static TestPin pins[] = {
    // name     gpio input  boot mode
    {"TX1",      2, false, M_HIGH},     // RS232 to servo 1 (HIGH = idle)
    {"RX1",      3, true,  M_PULLUP},   // RS232 from servo 1
    {"STEP1",    4, false, M_LOW},
    {"DIR1",     5, false, M_LOW},
    {"ALM1",     6, true,  M_PULLUP},   // servo 1 alarm output
    {"TX2",     10, false, M_HIGH},
    {"RX2",      9, true,  M_PULLUP},
    {"STEP2",   11, false, M_LOW},
    {"DIR2",    12, false, M_LOW},
};
static const int PIN_COUNT = sizeof(pins) / sizeof(pins[0]);

static bool monitorOn = true;

struct Probe {
    HardwareSerial* uart;
    int txIndex;
    int rxIndex;
    uint32_t intervalMs;   // 0 = off
    uint32_t lastSendMs;
    TestMode savedTx;
    TestMode savedRx;
    uint32_t requests;
    uint32_t answers;
};
static Probe probes[2] = {
    {&Serial1, 0, 1, 0, 0, M_HIGH, M_PULLUP, 0, 0},
    {&Serial2, 5, 6, 0, 0, M_HIGH, M_PULLUP, 0, 0},
};

// ----------------------------------------------------------------------------

static void IRAM_ATTR onEdge(void* arg) {
    ((TestPin*)arg)->edges++;
}

static int findPin(const String& name) {
    for (int i = 0; i < PIN_COUNT; i++) {
        if (name.equalsIgnoreCase(pins[i].name)) return i;
    }
    return -1;
}

static void applyMode(int i, TestMode mode) {
    TestPin& p = pins[i];
    detachInterrupt(p.gpio);
    switch (mode) {
        case M_LOW:      pinMode(p.gpio, OUTPUT); digitalWrite(p.gpio, LOW); break;
        case M_HIGH:     pinMode(p.gpio, OUTPUT); digitalWrite(p.gpio, HIGH); break;
        case M_FLOAT:    pinMode(p.gpio, INPUT); break;
        case M_PULLUP:   pinMode(p.gpio, INPUT_PULLUP); break;
        case M_PULLDOWN: pinMode(p.gpio, INPUT_PULLDOWN); break;
        case M_UART:     break; // configured by the UART
    }
    p.mode = mode;
    if (mode == M_FLOAT || mode == M_PULLUP || mode == M_PULLDOWN) {
        attachInterruptArg(p.gpio, onEdge, &p, CHANGE);
    }
    p.lastLevel = digitalRead(p.gpio);
}

static void printPin(int i) {
    TestPin& p = pins[i];
    Serial.printf("  %-6s GPIO%-2d  %-8s level %d  edges %lu\n", p.name, p.gpio, MODE_NAMES[p.mode],
                  digitalRead(p.gpio), (unsigned long)p.edges);
}

static void printStatus() {
    Serial.println(F("--- ServoPinTest, ControlBoard V7 ---"));
    for (int i = 0; i < PIN_COUNT; i++) printPin(i);
    Serial.printf("  BRAKE1 GPIO%d   LOW (fixed)\n", PIN_BRAKE_RESISTOR_1);
    for (int a = 0; a < 2; a++) {
        if (probes[a].intervalMs > 0) {
            Serial.printf("  PROBE %d every %lu ms: %lu requests, %lu answers\n", a + 1,
                          (unsigned long)probes[a].intervalMs, (unsigned long)probes[a].requests,
                          (unsigned long)probes[a].answers);
        }
    }
    Serial.printf("  MONITOR %s, uptime %.1f s\n", monitorOn ? "ON" : "OFF", millis() / 1000.0f);
}

static void printHelp() {
    Serial.println(F("Commands:"));
    Serial.println(F("  STATUS"));
    Serial.println(F("  <pin> <mode>      pins: TX1 RX1 STEP1 DIR1 ALM1 TX2 RX2 STEP2 DIR2"));
    Serial.println(F("                    modes: HIGH LOW FLOAT PULLUP PULLDOWN (RX/ALM: FLOAT PULLUP PULLDOWN)"));
    Serial.println(F("  PROBE <1|2> <ms>  Modbus request (slave 63) every <ms>, prints received bytes"));
    Serial.println(F("  PROBE <1|2> OFF"));
    Serial.println(F("  MONITOR ON|OFF    print level changes of the inputs"));
    Serial.println(F("Servo RX sees: TX HIGH = -5.5 V (idle), TX LOW = +5.5 V (break)."));
}

// Modbus RTU CRC16
static uint16_t crc16(const uint8_t* data, size_t len) {
    uint16_t crc = 0xFFFF;
    for (size_t i = 0; i < len; i++) {
        crc ^= data[i];
        for (int b = 0; b < 8; b++) crc = (crc & 1) ? (crc >> 1) ^ 0xA001 : crc >> 1;
    }
    return crc;
}

static void startProbe(int a, uint32_t intervalMs) {
    Probe& pr = probes[a];
    if (pr.intervalMs == 0) {
        pr.savedTx = pins[pr.txIndex].mode;
        pr.savedRx = pins[pr.rxIndex].mode;
        detachInterrupt(pins[pr.rxIndex].gpio);
        pr.uart->begin(MODBUS_BAUD, SERIAL_8N1, pins[pr.rxIndex].gpio, pins[pr.txIndex].gpio);
        pins[pr.txIndex].mode = M_UART;
        pins[pr.rxIndex].mode = M_UART;
    }
    pr.intervalMs = intervalMs;
    pr.lastSendMs = millis() - intervalMs;
    pr.requests = 0;
    pr.answers = 0;
    Serial.printf("Probe %d: Modbus request every %lu ms\n", a + 1, (unsigned long)intervalMs);
}

static void stopProbe(int a) {
    Probe& pr = probes[a];
    if (pr.intervalMs == 0) return;
    pr.uart->end();
    pr.intervalMs = 0;
    applyMode(pr.txIndex, pr.savedTx);
    applyMode(pr.rxIndex, pr.savedRx);
    Serial.printf("Probe %d off: %lu requests, %lu answers. TX%d %s, RX%d %s\n", a + 1,
                  (unsigned long)pr.requests, (unsigned long)pr.answers, a + 1, MODE_NAMES[pr.savedTx],
                  a + 1, MODE_NAMES[pr.savedRx]);
}

static void serviceProbe(int a) {
    Probe& pr = probes[a];
    if (pr.intervalMs == 0) return;

    // Print what the servo sends
    if (pr.uart->available()) {
        Serial.printf("[%8.3f s] RX%d:", millis() / 1000.0f, a + 1);
        int n = 0;
        uint32_t start = millis();
        while (millis() - start < 5) { // collect one frame
            while (pr.uart->available()) {
                Serial.printf(" %02X", pr.uart->read());
                n++;
                start = millis();
            }
        }
        Serial.println();
        if (n >= 5) pr.answers++;
    }

    if (millis() - pr.lastSendMs >= pr.intervalMs) {
        pr.lastSendMs = millis();
        uint8_t frame[8] = {MODBUS_SLAVE_ID, 0x03, 0x00, 0x00, 0x00, 0x02, 0, 0};
        uint16_t crc = crc16(frame, 6);
        frame[6] = crc & 0xFF;
        frame[7] = crc >> 8;
        pr.uart->write(frame, sizeof(frame));
        pr.requests++;
    }
}

static void serviceMonitor() {
    static uint32_t lastCheckMs = 0;
    if (millis() - lastCheckMs < 10) return;
    lastCheckMs = millis();
    for (int i = 0; i < PIN_COUNT; i++) {
        TestPin& p = pins[i];
        if (p.mode == M_UART || p.mode == M_LOW || p.mode == M_HIGH) continue;
        int level = digitalRead(p.gpio);
        uint32_t edges = p.edges;
        if (monitorOn && (level != p.lastLevel || edges != p.reportedEdges)) {
            Serial.printf("[%8.3f s] %s level %d (%lu new edges)\n", millis() / 1000.0f, p.name, level,
                          (unsigned long)(edges - p.reportedEdges));
        }
        p.lastLevel = level;
        p.reportedEdges = edges;
    }
}

static bool parseMode(const String& s, TestMode& mode) {
    for (int m = 0; m <= M_PULLDOWN; m++) {
        if (s.equalsIgnoreCase(MODE_NAMES[m])) {
            mode = (TestMode)m;
            return true;
        }
    }
    return false;
}

static void handleCommand(String line) {
    line.trim();
    if (line.length() == 0) return;
    int sp = line.indexOf(' ');
    String cmd = sp < 0 ? line : line.substring(0, sp);
    String arg = sp < 0 ? "" : line.substring(sp + 1);
    arg.trim();
    cmd.toUpperCase();
    arg.toUpperCase();

    if (cmd == "STATUS") {
        printStatus();
    } else if (cmd == "HELP" || cmd == "?") {
        printHelp();
    } else if (cmd == "MONITOR") {
        monitorOn = (arg != "OFF");
        Serial.printf("Monitor %s\n", monitorOn ? "ON" : "OFF");
    } else if (cmd == "PROBE") {
        int sp2 = arg.indexOf(' ');
        int axis = (sp2 < 0 ? arg : arg.substring(0, sp2)).toInt();
        String value = sp2 < 0 ? "" : arg.substring(sp2 + 1);
        if (axis < 1 || axis > 2 || value.length() == 0) {
            Serial.println(F("Usage: PROBE <1|2> <ms> | PROBE <1|2> OFF"));
        } else if (value == "OFF") {
            stopProbe(axis - 1);
        } else if (value.toInt() >= 5) {
            startProbe(axis - 1, value.toInt());
        } else {
            Serial.println(F("Interval must be >= 5 ms"));
        }
    } else {
        int i = findPin(cmd);
        TestMode mode;
        if (i < 0 || !parseMode(arg, mode)) {
            Serial.println(F("Unknown command. HELP lists the commands."));
            return;
        }
        if (pins[i].mode == M_UART) {
            Serial.println(F("Pin is used by PROBE, stop it first (PROBE <axis> OFF)."));
            return;
        }
        if (pins[i].inputOnly && (mode == M_LOW || mode == M_HIGH)) {
            Serial.printf("%s is driven by the board (RS232 receiver / servo output) and must stay an input: FLOAT, PULLUP or PULLDOWN.\n",
                          pins[i].name);
            Serial.println(F("The level at the servo RX is set with TX1 / TX2."));
            return;
        }
        applyMode(i, mode);
        printPin(i);
    }
}

void setup() {
    // Pins first, before USB comes up
    pinMode(PIN_BRAKE_RESISTOR_1, OUTPUT);
    digitalWrite(PIN_BRAKE_RESISTOR_1, LOW);
    for (int i = 0; i < PIN_COUNT; i++) {
        applyMode(i, pins[i].mode);
    }

    Serial.begin(115200);
    uint32_t start = millis();
    while (!Serial && millis() - start < 3000) delay(10);
    Serial.println();
    Serial.println(F("ServoPinTest ready. Note: before setup() the pins float for ~100 ms after a reset."));
    printHelp();
    printStatus();
}

void loop() {
    static String line;
    while (Serial.available()) {
        char c = (char)Serial.read();
        if (c == '\n' || c == '\r') {
            if (line.length()) handleCommand(line);
            line = "";
        } else if (line.length() < 80) {
            line += c;
        }
    }
    serviceProbe(0);
    serviceProbe(1);
    serviceMonitor();
}
