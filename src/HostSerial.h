#pragma once
#include <Arduino.h>

// Stream used for the SimHub protocol.
//
// Native USB boards need two workarounds, because the SimHub Motion plugin opens
// the port with DTR low and has no setting to change that:
//
// 1. USBCDC::write() silently drops all data while DTR is low, so replies are
//    written directly to TinyUSB instead.
// 2. SimHub waits for the boot greeting before it sends its first command. A
//    CP2102 board resets when the port opens, so the greeting arrives at that
//    moment. A native USB board keeps running and cannot detect the open, so it
//    repeats the greeting until the host sends something. SimHub clears its
//    receive buffer before each query, so extra greetings do not disturb it.
//
// UART boards (CP2102) need neither and use Serial unchanged.
#if ARDUINO_USB_CDC_ON_BOOT && !ARDUINO_USB_MODE

#include "esp32-hal-tinyusb.h"

// Silence on RX after which the host counts as gone. Together with the interval
// this must stay well below SimHub's 3 s handshake timeout.
static const uint32_t HOST_GREETING_QUIET_MS = 1000;
static const uint32_t HOST_GREETING_INTERVAL_MS = 250;

class HostSerialStream : public Stream {
public:
    int available() override { return Serial.available(); }
    int peek() override { return Serial.peek(); }
    void flush() override { Serial.flush(); }

    int read() override {
        int value = Serial.read();
        if (value >= 0) {
            lastRxMs = millis();
        }
        return value;
    }

    size_t write(uint8_t c) override { return write(&c, 1); }

    size_t write(const uint8_t* buffer, size_t size) override {
        if (tud_cdc_n_connected(0)) {
            return Serial.write(buffer, size);
        }
        size_t sent = tud_cdc_n_write(0, buffer, size);
        tud_cdc_n_write_flush(0);
        return sent;
    }

    bool greetingDue() {
        if (!tud_ready() || tud_cdc_n_connected(0)) {
            return false;
        }
        uint32_t now = millis();
        if (now - lastRxMs < HOST_GREETING_QUIET_MS || now - lastGreetingMs < HOST_GREETING_INTERVAL_MS) {
            return false;
        }
        lastGreetingMs = now;
        // Nobody may be reading: keep only the latest greeting instead of a backlog
        tud_cdc_n_write_clear(0);
        return true;
    }

private:
    uint32_t lastRxMs = 0;
    uint32_t lastGreetingMs = 0;
};

static HostSerialStream hostSerial;

static inline bool hostGreetingDue() { return hostSerial.greetingDue(); }

#else

static Stream& hostSerial = Serial;

static inline bool hostGreetingDue() { return false; }

#endif
