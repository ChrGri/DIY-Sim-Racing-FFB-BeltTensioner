#include "BeltTensionerProtocol.h"

BeltTensionerProtocol::BeltTensionerProtocol() {
    axisUnits = nullptr;
    numAxes = 0;
    parserState = PARSE_IDLE;
    currentCmd = 0;
    dataExpectedLen = 0;
    dataIndex = 0;
    lastByteTime = 0;
}

void BeltTensionerProtocol::begin(AxisUnit** axes, uint8_t axisCount) {
    axisUnits = axes;
    numAxes = axisCount;
}

void BeltTensionerProtocol::sendGreeting(Stream* stream) {
    if (stream == nullptr) return;
    stream->print(String(numAxes));
    stream->println(F(" steppers enabled"));
}

void BeltTensionerProtocol::dumpSensor(Stream* stream, uint8_t axis) {
    if (stream == nullptr || axis >= numAxes || axisUnits == nullptr) return;

    int16_t load = axisUnits[axis]->getCurrentLoadPercent();
    uint8_t threshold = axisUnits[axis]->settings.homingCurrentThreshold;
    uint8_t triggered = (abs(load) >= threshold) ? 1 : 0;

    stream->print(F("Sensor #"));
    stream->print(axis);
    stream->print(':');
    stream->print(load);
    stream->print(F(":Trigger level:"));
    stream->print(threshold);
    stream->print(F(":Triggered:"));
    stream->print(triggered);
    stream->println();
}

void BeltTensionerProtocol::handleCommand(Stream* stream) {
    switch (currentCmd) {
        case 1: { // CMD 1: Set Target Position
            uint16_t val1 = (uint16_t)(((uint16_t)dataBuffer[0] << 8) | (uint16_t)dataBuffer[1]);
            uint16_t val2 = (uint16_t)(((uint16_t)dataBuffer[2] << 8) | (uint16_t)dataBuffer[3]);
            if (numAxes > 0 && axisUnits[0] != nullptr) {
                axisUnits[0]->setPosition16Bits(val1);
            }
            if (numAxes > 1 && axisUnits[1] != nullptr) {
                axisUnits[1]->setPosition16Bits(val2);
            }
            break;
        }

        case 2: { // CMD 2: Set Max Speed
            uint16_t val1 = (uint16_t)(((uint16_t)dataBuffer[0] << 8) | (uint16_t)dataBuffer[1]);
            uint16_t val2 = (uint16_t)(((uint16_t)dataBuffer[2] << 8) | (uint16_t)dataBuffer[3]);
            if (numAxes > 0 && axisUnits[0] != nullptr) {
                axisUnits[0]->setMaxSpeed16Bits(val1);
            }
            if (numAxes > 1 && axisUnits[1] != nullptr) {
                axisUnits[1]->setMaxSpeed16Bits(val2);
            }
            break;
        }

        case 3: { // CMD 3: Set Max Acceleration
            uint32_t val1 = ((uint32_t)dataBuffer[0] << 24) |
                            ((uint32_t)dataBuffer[1] << 16) |
                            ((uint32_t)dataBuffer[2] << 8)  |
                            ((uint32_t)dataBuffer[3]);
            uint32_t val2 = ((uint32_t)dataBuffer[4] << 24) |
                            ((uint32_t)dataBuffer[5] << 16) |
                            ((uint32_t)dataBuffer[6] << 8)  |
                            ((uint32_t)dataBuffer[7]);
            if (numAxes > 0 && axisUnits[0] != nullptr) {
                axisUnits[0]->setMaxAcceleration32Bits(val1);
            }
            if (numAxes > 1 && axisUnits[1] != nullptr) {
                axisUnits[1]->setMaxAcceleration32Bits(val2);
            }
            break;
        }

        case 10: { // CMD 10: Query Enabled Motors
            if (stream != nullptr) {
                stream->print(F("Enabled motors:"));
                stream->println(numAxes);
            }
            break;
        }

        case 11: { // CMD 11: Dump Sensor Diagnostic
            dumpSensor(stream, dataBuffer[0]);
            break;
        }

        case 12: { // CMD 12: Park Now (slack release)
            for (uint8_t i = 0; i < numAxes; i++) {
                if (axisUnits[i] != nullptr) {
                    axisUnits[i]->moveToIdle(false);
                }
            }
            break;
        }

        case 13: { // CMD 13: Recalibrate / Discard Calibration
            for (uint8_t i = 0; i < numAxes; i++) {
                if (axisUnits[i] != nullptr) {
                    axisUnits[i]->startSensorlessHoming();
                }
            }
            break;
        }

        case 14: { // CMD 14: Query Firmware Version
            if (stream != nullptr) {
                stream->println(F(FIRMWARE_VERSION));
            }
            break;
        }

        default:
            break;
    }
}

void BeltTensionerProtocol::processIncomingStream(Stream* stream) {
    if (stream == nullptr) return;

    // Reset parser if packet timed out (incomplete packet)
    if (parserState != PARSE_IDLE && (millis() - lastByteTime > 150)) {
        parserState = PARSE_IDLE;
    }

    while (stream->available()) {
        uint8_t b = (uint8_t)stream->read();
        lastByteTime = millis();

        switch (parserState) {
            case PARSE_IDLE:
                if (b == 0xFF) {
                    parserState = PARSE_HEADER_2;
                }
                break;

            case PARSE_HEADER_2:
                if (b == 0xFF) {
                    parserState = PARSE_CMD;
                } else if (b != 0xFF) {
                    parserState = PARSE_IDLE;
                }
                break;

            case PARSE_CMD:
                currentCmd = b;
                dataIndex = 0;

                if (currentCmd == 1 || currentCmd == 2) {
                    dataExpectedLen = 4; // 2x 16-bit uint
                    parserState = PARSE_DATA;
                } else if (currentCmd == 3) {
                    dataExpectedLen = 8; // 2x 32-bit uint
                    parserState = PARSE_DATA;
                } else if (currentCmd == 11) {
                    dataExpectedLen = 1; // 1 byte axis index
                    parserState = PARSE_DATA;
                } else if (currentCmd == 10 || currentCmd == 12 || currentCmd == 13 || currentCmd == 14) {
                    dataExpectedLen = 0;
                    parserState = PARSE_TERM_1;
                } else {
                    // Unknown command, reset
                    parserState = PARSE_IDLE;
                }
                break;

            case PARSE_DATA:
                dataBuffer[dataIndex++] = b;
                if (dataIndex >= dataExpectedLen) {
                    parserState = PARSE_TERM_1;
                }
                break;

            case PARSE_TERM_1:
                if (b == 0x0A) {
                    parserState = PARSE_TERM_2;
                } else {
                    parserState = (b == 0xFF) ? PARSE_HEADER_2 : PARSE_IDLE;
                }
                break;

            case PARSE_TERM_2:
                if (b == 0x0D) {
                    handleCommand(stream);
                }
                parserState = PARSE_IDLE;
                break;
        }
    }
}
