#include "WebGraphs.h"

#if ENABLE_WEBGRAPHS

#include <WiFi.h>
#include <WebServer.h>
#include <ESPmDNS.h>
#include <Preferences.h>
#include "WebGraphsPage.h"

WebGraphs webGraphs;

// Maximum number of samples returned by a single /data request
#define WEBGRAPHS_MAX_SAMPLES_PER_REQUEST 128
#define WEBGRAPHS_HOSTNAME "belttensioner"
#define WEBGRAPHS_NVS_NAMESPACE "webgraphs"

static WebServer* server = nullptr;
static WebGraphSample* scratch = nullptr;

// Network state (only modified by the web graphs task)
static char staSsid[33] = {0};
static char staPass[65] = {0};
static bool staFromNvs = false;          // Credentials come from NVS (web page) instead of Config.h
static volatile bool staWanted = false;  // Station mode is trying to connect / connected
static volatile bool staConnected = false;
static volatile bool staFailed = false;  // Last connection attempt timed out
static volatile bool apOn = false;
static uint32_t staStartTime = 0;
static uint32_t apOffTime = 0;           // 0 = keep access point open
static volatile bool reconnectRequested = false;
static uint32_t reconnectRequestTime = 0;
static bool mdnsRunning = false;

// ---------------------------------------------------------------------------
// Credentials in NVS
// ---------------------------------------------------------------------------

static void loadCredentials() {
    Preferences prefs;
    staFromNvs = false;
    staSsid[0] = '\0';
    staPass[0] = '\0';
    if (prefs.begin(WEBGRAPHS_NVS_NAMESPACE, true)) {
        if (prefs.isKey("ssid")) {
            prefs.getString("ssid", staSsid, sizeof(staSsid));
            if (prefs.isKey("pass")) {
                prefs.getString("pass", staPass, sizeof(staPass));
            }
            staFromNvs = (staSsid[0] != '\0');
        }
        prefs.end();
    }
    if (!staFromNvs) {
        strlcpy(staSsid, WEBGRAPHS_STA_SSID, sizeof(staSsid));
        strlcpy(staPass, WEBGRAPHS_STA_PASSWORD, sizeof(staPass));
    }
}

static bool saveCredentials(const String& ssid, const String& pass) {
    Preferences prefs;
    if (!prefs.begin(WEBGRAPHS_NVS_NAMESPACE, false)) return false;
    bool ok = (prefs.putString("ssid", ssid) == ssid.length()) &&
              (prefs.putString("pass", pass) == pass.length());
    prefs.end();
    return ok;
}

static void clearCredentials() {
    Preferences prefs;
    if (prefs.begin(WEBGRAPHS_NVS_NAMESPACE, false)) {
        prefs.clear();
        prefs.end();
    }
}

// ---------------------------------------------------------------------------
// WiFi state machine
// ---------------------------------------------------------------------------

static void stopMdns() {
    if (mdnsRunning) {
        MDNS.end();
        mdnsRunning = false;
    }
}

static void startAp() {
    WiFi.mode(staWanted ? WIFI_AP_STA : WIFI_AP);
    WiFi.softAP(WEBGRAPHS_AP_SSID, (strlen(WEBGRAPHS_AP_PASSWORD) >= 8) ? WEBGRAPHS_AP_PASSWORD : nullptr);
    apOn = true;
    apOffTime = 0;
}

static void stopAp() {
    WiFi.softAPdisconnect(true);
    apOn = false;
    apOffTime = 0;
}

// Starts joining the configured network. keepAp keeps the access point open meanwhile,
// so a browser connected to it can follow the connection attempt.
static void startStation(bool keepAp) {
    stopMdns();
    if (staWanted) WiFi.disconnect();
    staConnected = false;
    staFailed = false;

    if (staSsid[0] == '\0') {
        staWanted = false;
        if (apOn) {
            WiFi.mode(WIFI_AP);
        } else {
            startAp();
        }
        return;
    }

    staWanted = true;
    if (keepAp && apOn) {
        WiFi.mode(WIFI_AP_STA);
    } else {
        if (apOn) stopAp();
        WiFi.mode(WIFI_STA);
    }
    WiFi.begin(staSsid, (staPass[0] != '\0') ? staPass : nullptr);
    staStartTime = millis();
}

static void updateNetwork() {
    if (staWanted) {
        bool connected = (WiFi.status() == WL_CONNECTED);
        if (connected && !staConnected) {
            staConnected = true;
            staFailed = false;
            WiFi.setSleep(false); // Lower latency for live graphs
            mdnsRunning = MDNS.begin(WEBGRAPHS_HOSTNAME);
            if (mdnsRunning) MDNS.addService("http", "tcp", 80);
            if (apOn) apOffTime = millis() + WEBGRAPHS_AP_LINGER_MS;
        } else if (!connected && staConnected) {
            // Connection lost: WiFi auto-reconnect runs, access point opens after the timeout
            staConnected = false;
            stopMdns();
            staStartTime = millis();
        }

        if (!staConnected && (millis() - staStartTime) > WEBGRAPHS_STA_TIMEOUT_MS) {
            staFailed = true;
            staWanted = false;
            WiFi.disconnect();
            if (apOn) {
                WiFi.mode(WIFI_AP);
            } else {
                startAp();
            }
        }
    }

    if (staConnected && apOn && apOffTime != 0 && (int32_t)(millis() - apOffTime) >= 0) {
        stopAp();
    }
}

// ---------------------------------------------------------------------------
// WebGraphs
// ---------------------------------------------------------------------------

void WebGraphs::begin(AxisUnit** axes, uint8_t axisCount) {
    axisUnits = axes;
    numAxes = (axisCount > WEBGRAPHS_MAX_AXES) ? WEBGRAPHS_MAX_AXES : axisCount;
}

void WebGraphs::activate() {
    if (taskHandle != nullptr) return;

    buffer = (WebGraphSample*)calloc(WEBGRAPHS_BUFFER_SIZE, sizeof(WebGraphSample));
    scratch = (WebGraphSample*)calloc(WEBGRAPHS_MAX_SAMPLES_PER_REQUEST, sizeof(WebGraphSample));
    if (buffer == nullptr || scratch == nullptr) {
        free(buffer);
        free(scratch);
        buffer = nullptr;
        scratch = nullptr;
        return;
    }

    // WiFi bring-up can take several seconds, so it runs in its own task instead of the SimHub loop
    xTaskCreatePinnedToCore(taskEntry, "webGraphs", 8192, this, 1, &taskHandle, TASK_CORE_WEBGRAPHS);
}

void WebGraphs::printStatus(Stream* logStream) {
    if (logStream == nullptr) return;
    if (!active) {
        logStream->println((taskHandle != nullptr) ? F("Web graphs: starting...") : F("Web graphs: not running (out of memory)"));
        return;
    }
    if (staConnected) {
        logStream->print(F("Web graphs: connected to WiFi \""));
        logStream->print(staSsid);
        logStream->print(F("\", open http://"));
        logStream->print(WiFi.localIP());
        logStream->println(F("/ or http://" WEBGRAPHS_HOSTNAME ".local/"));
    } else if (staWanted) {
        logStream->print(F("Web graphs: connecting to WiFi \""));
        logStream->print(staSsid);
        logStream->println(F("\"..."));
    }
    if (apOn) {
        logStream->print(F("Web graphs: access point \""));
        logStream->print(WEBGRAPHS_AP_SSID);
        if (strlen(WEBGRAPHS_AP_PASSWORD) >= 8) {
            logStream->print(F("\" (password \""));
            logStream->print(WEBGRAPHS_AP_PASSWORD);
            logStream->print(F("\")"));
        } else {
            logStream->print(F("\" (open)"));
        }
        logStream->print(F(", open http://"));
        logStream->print(WiFi.softAPIP());
        logStream->println('/');
    }
}

void WebGraphs::recordSample() {
    if (!active || buffer == nullptr || axisUnits == nullptr) return;

    WebGraphSample sample;
    sample.timeMs = millis();
    for (uint8_t i = 0; i < numAxes; i++) {
        AxisUnit* axis = axisUnits[i];
        if (axis == nullptr) continue;
        WebGraphAxisSample& s = sample.axis[i];
        const Isv57DynamicStates& servo = axis->isv57.dynamicStates;
        s.stepperPos = axis->getCurrentPosition();
        s.targetPos = axis->state.targetPosition;
        s.servoPos = axis->servoPosCorrected_i32;
        s.speedHz = axis->getCurrentSpeedHz();
        s.posError = servo.servo_pos_error_p;
        s.currentPct = servo.servo_current_percent;
        s.voltage0p1V = servo.servoVoltage0p1V_i16;
        s.homingState = (uint8_t)axis->state.homingState;
        s.flags = (servo.servo_receivedPacketIsValid_b ? 0x01 : 0) |
                  (axis->isHomed() ? 0x02 : 0) |
                  (axis->state.motorPowered ? 0x04 : 0);
    }

    portENTER_CRITICAL(&bufferMux);
    buffer[writeSeq % WEBGRAPHS_BUFFER_SIZE] = sample;
    writeSeq++;
    portEXIT_CRITICAL(&bufferMux);
}

uint32_t WebGraphs::copySamples(uint32_t sinceSeq, WebGraphSample* out, uint32_t maxCount, uint32_t& lastSeq) {
    portENTER_CRITICAL(&bufferMux);
    uint32_t oldest = (writeSeq > WEBGRAPHS_BUFFER_SIZE) ? (writeSeq - WEBGRAPHS_BUFFER_SIZE) : 0;
    uint32_t start = (sinceSeq < oldest || sinceSeq > writeSeq) ? oldest : sinceSeq;
    uint32_t count = writeSeq - start;
    if (count > maxCount) count = maxCount;
    for (uint32_t i = 0; i < count; i++) {
        out[i] = buffer[(start + i) % WEBGRAPHS_BUFFER_SIZE];
    }
    lastSeq = start + count;
    portEXIT_CRITICAL(&bufferMux);
    return count;
}

// ---------------------------------------------------------------------------
// HTTP handlers
// ---------------------------------------------------------------------------

static void appendJsonString(String& out, const char* s) {
    out += '"';
    for (; *s != '\0'; s++) {
        char c = *s;
        if (c == '"' || c == '\\') {
            out += '\\';
            out += c;
        } else if ((uint8_t)c < 0x20) {
            char esc[8];
            snprintf(esc, sizeof(esc), "\\u%04x", (unsigned)c);
            out += esc;
        } else {
            out += c;
        }
    }
    out += '"';
}

static void sendJson(int code, const String& json) {
    server->sendHeader(F("Cache-Control"), F("no-store"));
    server->send(code, "application/json", json);
}

static void sendError(const char* msg) {
    String json = F("{\"ok\":false,\"error\":");
    appendJsonString(json, msg);
    json += '}';
    sendJson(400, json);
}

static void handleRoot() {
    server->send_P(200, "text/html", WEBGRAPHS_INDEX_HTML);
}

static void handleInfo() {
    String json;
    json.reserve(384);
    json += F("{\"version\":\"" FIRMWARE_VERSION "\",\"axes\":");
    json += webGraphs.getAxisCount();
    json += F(",\"stepsPerMm\":[");
    for (uint8_t i = 0; i < webGraphs.getAxisCount(); i++) {
        if (i > 0) json += ',';
        json += String(webGraphs.getAxes()[i]->settings.mech.stepsPerMm(), 3);
    }
    json += ']';
    json += F(",\"stepsPerRev\":");
    json += STEPS_PER_MOTOR_REV;
    // Motor ratings per axis (Pr7.10 / Pr7.12 / Pr7.14, read from the servo once it is connected)
    json += F(",\"rated\":[");
    for (uint8_t i = 0; i < webGraphs.getAxisCount(); i++) {
        const Isv57MotorRatings& r = webGraphs.getAxes()[i]->isv57.motorRatings;
        if (i > 0) json += ',';
        json += F("{\"powerW\":");
        json += r.ratedPowerW;
        json += F(",\"rpm\":");
        json += r.ratedSpeedRpm;
        json += F(",\"currentA\":");
        json += String(r.ratedCurrent0p01A * 0.01f, 2);
        json += F(",\"fromServo\":");
        json += r.readFromServo ? F("true") : F("false");
        json += '}';
    }
    json += ']';
    json += F(",\"minPct\":");
    json += String(MIN_SAFETY_LIMIT_PCT, 1);
    json += F(",\"maxPct\":");
    json += String(MAX_SAFETY_LIMIT_PCT, 1);
    json += F(",\"threshold\":[");
    for (uint8_t i = 0; i < webGraphs.getAxisCount(); i++) {
        if (i > 0) json += ',';
        json += webGraphs.getAxes()[i]->settings.homingCurrentThreshold;
    }
    json += F("],\"range\":[");
    for (uint8_t i = 0; i < webGraphs.getAxisCount(); i++) {
        if (i > 0) json += ',';
        json += webGraphs.getAxes()[i]->getWorkingRange();
    }
    json += F("]}");
    sendJson(200, json);
}

// Returns all samples newer than ?since=<seq> as
// {"seq":<next seq>,"s":[[t, pos, target, servo, err, cur, volt, homing, flags, speed, (axis 2 ...)], ...]}
static void handleData() {
    uint32_t since = (uint32_t)strtoul(server->arg("since").c_str(), nullptr, 10);
    uint32_t lastSeq = 0;
    uint32_t count = webGraphs.copySamples(since, scratch, WEBGRAPHS_MAX_SAMPLES_PER_REQUEST, lastSeq);
    uint8_t axes = webGraphs.getAxisCount();

    String json;
    json.reserve(32 + count * (12 + axes * 56));
    json += F("{\"seq\":");
    json += lastSeq;
    json += F(",\"s\":[");
    for (uint32_t i = 0; i < count; i++) {
        const WebGraphSample& sample = scratch[i];
        if (i > 0) json += ',';
        json += '[';
        json += sample.timeMs;
        for (uint8_t a = 0; a < axes; a++) {
            const WebGraphAxisSample& s = sample.axis[a];
            json += ',';
            json += s.stepperPos;
            json += ',';
            json += s.targetPos;
            json += ',';
            json += s.servoPos;
            json += ',';
            json += s.posError;
            json += ',';
            json += s.currentPct;
            json += ',';
            json += s.voltage0p1V;
            json += ',';
            json += s.homingState;
            json += ',';
            json += s.flags;
            json += ',';
            json += s.speedHz;
        }
        json += ']';
    }
    json += F("]}");
    sendJson(200, json);
}

static void handleWifiStatus() {
    String json;
    json.reserve(320);
    json += F("{\"ap\":");
    json += apOn ? F("true") : F("false");
    json += F(",\"apSsid\":");
    appendJsonString(json, WEBGRAPHS_AP_SSID);
    json += F(",\"apIp\":\"");
    json += WiFi.softAPIP().toString();
    json += F("\",\"ssid\":");
    appendJsonString(json, staSsid);
    json += F(",\"saved\":");
    json += staFromNvs ? F("true") : F("false");
    json += F(",\"connecting\":");
    json += (staWanted && !staConnected) ? F("true") : F("false");
    json += F(",\"connected\":");
    json += staConnected ? F("true") : F("false");
    json += F(",\"failed\":");
    json += staFailed ? F("true") : F("false");
    json += F(",\"ip\":\"");
    json += staConnected ? WiFi.localIP().toString() : String();
    json += F("\",\"rssi\":");
    json += staConnected ? WiFi.RSSI() : 0;
    json += F(",\"host\":\"" WEBGRAPHS_HOSTNAME ".local\",\"apCloseIn\":");
    json += (apOn && apOffTime != 0) ? (int32_t)(apOffTime - millis()) / 1000 : -1;
    json += '}';
    sendJson(200, json);
}

// POST /wifi  action=save (ssid, pass) | connect | forget
static void handleWifiPost() {
    String action = server->arg("action");

    if (action == "save") {
        String ssid = server->arg("ssid");
        String pass = server->arg("pass");
        if (ssid.length() == 0 || ssid.length() > 32) {
            sendError("SSID must be 1 to 32 characters");
            return;
        }
        // Empty password field keeps the stored password of the same network
        if (pass.length() == 0 && ssid == staSsid) {
            pass = staPass;
        }
        if (pass.length() > 0 && (pass.length() < 8 || pass.length() > 63)) {
            sendError("Password must be 8 to 63 characters (or empty for an open network)");
            return;
        }
        if (!saveCredentials(ssid, pass)) {
            sendError("Could not write to flash (NVS)");
            return;
        }
    } else if (action == "forget") {
        clearCredentials();
    } else if (action != "connect") {
        sendError("Unknown action");
        return;
    }

    sendJson(200, F("{\"ok\":true}"));
    // Applied from the task loop, so this response reaches the browser before WiFi is switched
    reconnectRequestTime = millis();
    reconnectRequested = true;
}

// GET /scan?start=1 starts an asynchronous scan, GET /scan polls the result
static void handleScan() {
    if (server->hasArg("start")) {
        WiFi.scanDelete();
        WiFi.scanNetworks(true);
        sendJson(200, F("{\"running\":true}"));
        return;
    }

    int16_t n = WiFi.scanComplete();
    if (n == WIFI_SCAN_RUNNING) {
        sendJson(200, F("{\"running\":true}"));
        return;
    }

    String json;
    json.reserve(64 + (n > 0 ? n : 0) * 56);
    json += F("{\"running\":false,\"networks\":[");
    bool first = true;
    for (int16_t i = 0; i < n; i++) {
        String ssid = WiFi.SSID(i);
        if (ssid.length() == 0) continue; // Hidden network
        if (!first) json += ',';
        first = false;
        json += F("{\"ssid\":");
        appendJsonString(json, ssid.c_str());
        json += F(",\"rssi\":");
        json += WiFi.RSSI(i);
        json += F(",\"open\":");
        json += (WiFi.encryptionType(i) == WIFI_AUTH_OPEN) ? F("true") : F("false");
        json += '}';
    }
    json += F("]}");
    if (n >= 0) WiFi.scanDelete();
    sendJson(200, json);
}

// ---------------------------------------------------------------------------
// Task
// ---------------------------------------------------------------------------

void WebGraphs::taskEntry(void* param) {
    static_cast<WebGraphs*>(param)->runTask();
}

void WebGraphs::runTask() {
    WiFi.persistent(false);
    WiFi.setHostname(WEBGRAPHS_HOSTNAME);

    loadCredentials();
    startStation(false); // Opens the access point directly if no network is configured

    server = new WebServer(80);
    server->on("/", HTTP_GET, handleRoot);
    server->on("/info", HTTP_GET, handleInfo);
    server->on("/data", HTTP_GET, handleData);
    server->on("/wifi", HTTP_GET, handleWifiStatus);
    server->on("/wifi", HTTP_POST, handleWifiPost);
    server->on("/scan", HTTP_GET, handleScan);
    server->onNotFound([]() { server->send(404, "text/plain", "Not found"); });
    server->begin();

    active = true;

    while (true) {
        server->handleClient();

        if (reconnectRequested && (millis() - reconnectRequestTime) > 300) {
            reconnectRequested = false;
            loadCredentials();
            startStation(true);
        }

        updateNetwork();
        vTaskDelay(pdMS_TO_TICKS(2));
    }
}

#else

WebGraphs webGraphs;

void WebGraphs::begin(AxisUnit** axes, uint8_t axisCount) {
    axisUnits = axes;
    numAxes = axisCount;
}

void WebGraphs::activate() {}

void WebGraphs::printStatus(Stream* logStream) {
    if (logStream != nullptr) logStream->println(F("Web graphs are disabled in this firmware (ENABLE_WEBGRAPHS false)"));
}
void WebGraphs::recordSample() {}

uint32_t WebGraphs::copySamples(uint32_t, WebGraphSample*, uint32_t, uint32_t& lastSeq) {
    lastSeq = 0;
    return 0;
}

#endif
