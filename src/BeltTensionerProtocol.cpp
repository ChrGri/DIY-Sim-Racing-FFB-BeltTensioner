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

        case 15: { // CMD 15: Flash Tuned Parameters to Servo NVM
            for (uint8_t i = 0; i < numAxes; i++) {
                if (axisUnits[i] != nullptr) {
                    axisUnits[i]->flashTunedParameters(stream);
                }
            }
            break;
        }

        default:
            break;
    }
}

void BeltTensionerProtocol::handleAsciiCommand(Stream* stream, const char* cmd) {
    if (stream == nullptr || cmd == nullptr) return;

    if (strcasecmp(cmd, "FLASH_SERVO") == 0 || strcasecmp(cmd, "FLASH_SERVO 1") == 0 || strcasecmp(cmd, "FLASH_SERVO ALL") == 0 ||
        strcasecmp(cmd, "FLASH") == 0 || strcasecmp(cmd, "FLASH 1") == 0 || strcasecmp(cmd, "FLASH ALL") == 0) {
        stream->println(F(">>> Triggered Servo EEPROM Flash via Serial Command (FLASH_SERVO) <<<"));
        if (numAxes > 0 && axisUnits[0] != nullptr) {
            axisUnits[0]->flashTunedParameters(stream);
        }
    } else if (strcasecmp(cmd, "FLASH_SERVO 2") == 0 || strcasecmp(cmd, "FLASH 2") == 0) {
        if (numAxes > 1 && axisUnits[1] != nullptr) {
            axisUnits[1]->flashTunedParameters(stream);
        } else {
            stream->println(F("Actuator 2 is not enabled (NUM_ACTUATORS is 1)"));
        }
    } else if (strcasecmp(cmd, "ENABLE_SERVO") == 0 || strcasecmp(cmd, "ENABLE") == 0 || strcasecmp(cmd, "ENABLE 1") == 0) {
        stream->println(F(">>> Enabling Servo <<<"));
        if (numAxes > 0 && axisUnits[0] != nullptr) {
            axisUnits[0]->isv57.enableAxis();
            axisUnits[0]->enableMotor();
        }
    } else if (strcasecmp(cmd, "ENABLE_SERVO 2") == 0 || strcasecmp(cmd, "ENABLE 2") == 0) {
        if (numAxes > 1 && axisUnits[1] != nullptr) {
            axisUnits[1]->isv57.enableAxis();
            axisUnits[1]->enableMotor();
        }
    } else if (strcasecmp(cmd, "DISABLE_SERVO") == 0 || strcasecmp(cmd, "DISABLE") == 0) {
        stream->println(F(">>> Disabling Servo <<<"));
        for (uint8_t i = 0; i < numAxes; i++) {
            if (axisUnits[i] != nullptr) {
                axisUnits[i]->disableMotor();
                axisUnits[i]->isv57.disableAxis();
            }
        }
    } else if (strcasecmp(cmd, "HOME") == 0 || strcasecmp(cmd, "CALIBRATE") == 0) {
        stream->println(F(">>> Starting Homing Calibration <<<"));
        for (uint8_t i = 0; i < numAxes; i++) {
            if (axisUnits[i] != nullptr) {
                axisUnits[i]->startSensorlessHoming();
            }
        }
    } else if (strcasecmp(cmd, "HELP") == 0) {
        stream->println(F("Available Serial Commands:"));
        stream->println(F("  FLASH_SERVO    - Flash all 305 tuned parameters into iSV57 EEPROM/NVM"));
        stream->println(F("  ENABLE_SERVO   - Enable servo axis"));
        stream->println(F("  DISABLE_SERVO  - Disable servo axis"));
        stream->println(F("  HOME           - Re-run sensorless homing calibration"));
        stream->println(F("  STATUS         - Print live sensor load & voltage"));
    } else if (strcasecmp(cmd, "STATUS") == 0) {
        for (uint8_t i = 0; i < numAxes; i++) {
            dumpSensor(stream, i);
        }
    }
}

void BeltTensionerProtocol::processIncomingStream(Stream* stream) {
    if (stream == nullptr) return;

    // Reset parser if packet timed out (incomplete binary packet)
    if (parserState != PARSE_IDLE && (millis() - lastByteTime > 150)) {
        parserState = PARSE_IDLE;
        asciiCmdIndex = 0;
    }

    // Process ASCII command if idle timeout reached (e.g. Serial monitor set to "No Line Ending")
    if (parserState == PARSE_IDLE && asciiCmdIndex > 0 && (millis() - lastByteTime > 100)) {
        asciiCmdBuffer[asciiCmdIndex] = '\0';
        handleAsciiCommand(stream, asciiCmdBuffer);
        asciiCmdIndex = 0;
    }

    while (stream->available()) {
        uint8_t b = (uint8_t)stream->read();
        lastByteTime = millis();

        switch (parserState) {
            case PARSE_IDLE:
                if (b == 0xFF) {
                    parserState = PARSE_HEADER_2;
                    asciiCmdIndex = 0;
                } else if (b == '\r' || b == '\n') {
                    if (asciiCmdIndex > 0) {
                        asciiCmdBuffer[asciiCmdIndex] = '\0';
                        handleAsciiCommand(stream, asciiCmdBuffer);
                        asciiCmdIndex = 0;
                    }
                } else if (b >= 32 && b <= 126) {
                    if (asciiCmdIndex < sizeof(asciiCmdBuffer) - 1) {
                        asciiCmdBuffer[asciiCmdIndex++] = (char)b;
                    }
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
                } else if (currentCmd == 10 || currentCmd == 12 || currentCmd == 13 || currentCmd == 14 || currentCmd == 15) {
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
