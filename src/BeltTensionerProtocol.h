#pragma once

#include <Arduino.h>
#include "AxisUnit.h"
#include "Config.h"

enum ProtocolParserState {
    PARSE_IDLE,
    PARSE_HEADER_2,
    PARSE_CMD,
    PARSE_DATA,
    PARSE_TERM_1,
    PARSE_TERM_2
};

class BeltTensionerProtocol {
public:
    BeltTensionerProtocol();

    void begin(AxisUnit** axes, uint8_t axisCount);
    void processIncomingStream(Stream* stream);
    void sendGreeting(Stream* stream);

private:
    AxisUnit** axisUnits = nullptr;
    uint8_t numAxes = 0;

    ProtocolParserState parserState = PARSE_IDLE;
    uint8_t currentCmd = 0;
    uint8_t dataBuffer[16] = {0};
    uint8_t dataExpectedLen = 0;
    uint8_t dataIndex = 0;
    unsigned long lastByteTime = 0;

    char asciiCmdBuffer[32] = {0};
    uint8_t asciiCmdIndex = 0;

    void handleCommand(Stream* stream);
    void handleAsciiCommand(Stream* stream, const char* cmd);
    void dumpSensor(Stream* stream, uint8_t axis);
};
