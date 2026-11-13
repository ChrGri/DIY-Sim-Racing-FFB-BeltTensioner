#include "BeltTensionerProtocol.h"
#include "ControllerConfig.h"
#include "WebGraphs.h"

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

void BeltTensionerProtocol::flushStream(Stream* stream) {
    if (stream == nullptr) return;
    while (stream->available()) {
        stream->read();
    }
    parserState = PARSE_IDLE;
    asciiCmdIndex = 0;
    dataIndex = 0;
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

// Prints spindle pitch & working range per axis (saved in NVS or Config.h default)
void BeltTensionerProtocol::printAxisConfig(Stream* stream) {
    for (uint8_t i = 0; i < numAxes; i++) {
        AxisMechConfig saved = AxisConfig::load(i);
        stream->print(F("Axis "));
        stream->print(i + 1);
        stream->print(F(": spindle pitch "));
        stream->print(saved.spindlePitchMm, 2);
        stream->print(saved.pitchSaved ? F(" mm (saved)") : F(" mm (Config.h)"));
        stream->print(F(", working range "));
        stream->print(saved.workingRangeMm, 2);
        stream->print(saved.rangeSaved ? F(" mm (saved)") : F(" mm (Config.h)"));
        stream->print(F(" = "));
        stream->print(saved.workingRangeSteps());
        stream->println(F(" steps"));

        if (axisUnits != nullptr && axisUnits[i] != nullptr) {
            const AxisUnitSettings& s = axisUnits[i]->settings;
            stream->print(F("  motion: max speed "));
            stream->print(s.maxSpeedMS, 3);
            stream->print(F(" m/s ("));
            stream->print(s.maxSpeed);
            stream->print(F(" steps/s), acceleration "));
            stream->print(s.accelerationMS2, 2);
            stream->print(F(" m/s^2 ("));
            stream->print(s.acceleration);
            stream->println(F(" steps/s^2)"));

            const AxisMechConfig& active = s.mech;
            if (active.spindlePitchMm != saved.spindlePitchMm || active.workingRangeMm != saved.workingRangeMm) {
                stream->print(F("  -> changed, restart the controller to apply (active: pitch "));
                stream->print(active.spindlePitchMm, 2);
                stream->print(F(" mm, range "));
                stream->print(active.workingRangeMm, 2);
                stream->println(F(" mm)"));
            }
        }
    }
}

// Parses "<axis> <mm>" or "<mm>" (axis 1). Accepts ',' as decimal separator.
static bool parseAxisAndValue(const char* args, uint8_t& axisIndex, float& value) {
    char buf[32];
    strlcpy(buf, args, sizeof(buf));
    for (char* p = buf; *p != '\0'; p++) {
        if (*p == ',') *p = '.';
    }

    char* end1 = nullptr;
    float first = strtof(buf, &end1);
    if (end1 == buf) return false;

    char* end2 = nullptr;
    float second = strtof(end1, &end2);
    char* rest = end2;
    if (end2 == end1) {
        axisIndex = 0;
        value = first;
        rest = end1;
    } else {
        if (first != 1.0f && first != 2.0f) return false;
        axisIndex = (uint8_t)first - 1;
        value = second;
    }

    while (*rest == ' ') rest++;
    return (*rest == '\0');
}

// Parses an optional servo number (1/2) at the start of args; returns the axis index or -1
static int8_t parseServoIndex(const char*& args) {
    while (*args == ' ') args++;
    if (*args == '\0') return 0; // default: servo 1
    if ((args[0] == '1' || args[0] == '2') && (args[1] == ' ' || args[1] == '\0')) {
        int8_t index = (int8_t)(args[0] - '1');
        args++;
        while (*args == ' ') args++;
        return index;
    }
    return -1;
}

// SERVO_WRITE <servo> <register> <value>: register decimal or 0x hex, value -32768..32767
// Replies "SERVO_WRITE <servo> 0x<reg> = <value> OK" or "... failed (read back <v>)"
void BeltTensionerProtocol::servoWrite(Stream* stream, const char* args) {
    int8_t index = parseServoIndex(args);
    char* end = nullptr;
    long reg = strtol(args, &end, 0);
    bool valid = (index >= 0) && (end != args);
    long value = 0;
    if (valid) {
        const char* valueText = end;
        value = strtol(valueText, &end, 0);
        while (end != nullptr && *end == ' ') end++;
        valid = (end != valueText) && (end != nullptr) && (*end == '\0') &&
                reg >= 0 && reg < 0x0200 && value >= -32768 && value <= 32767;
    }
    if (!valid) {
        stream->println(F("Usage: SERVO_WRITE <servo> <register> <value>   e.g. SERVO_WRITE 1 0x0008 3200"));
        return;
    }
    if (index >= numAxes || axisUnits[index] == nullptr) {
        stream->println(F("Actuator 2 is not enabled (NUM_ACTUATORS is 1)"));
        return;
    }

    int16_t readBack = 0;
    bool ok = axisUnits[index]->writeServoParameter((uint16_t)reg, (int16_t)value, &readBack);
    char line[64];
    snprintf(line, sizeof(line), "SERVO_WRITE %d 0x%04X = %ld %s", index + 1, (unsigned)reg, value, ok ? "OK" : "failed");
    stream->print(line);
    if (!ok) {
        stream->print(F(" (read back "));
        stream->print(readBack);
        stream->print(')');
    }
    stream->println();
}

// SERVO_SAVE [servo]: stores the servo RAM parameters permanently in its EEPROM
void BeltTensionerProtocol::servoSave(Stream* stream, const char* args) {
    int8_t index = parseServoIndex(args);
    if (index < 0 || *args != '\0') {
        stream->println(F("Usage: SERVO_SAVE <servo>"));
        return;
    }
    if (index >= numAxes || axisUnits[index] == nullptr) {
        stream->println(F("Actuator 2 is not enabled (NUM_ACTUATORS is 1)"));
        return;
    }
    bool ok = axisUnits[index]->saveServoParameters();
    stream->print(F("SERVO_SAVE "));
    stream->print(index + 1);
    stream->println(ok ? F(" OK - power cycle the servo to activate all values") : F(" failed (no ACK from the servo)"));
}

void BeltTensionerProtocol::setAxisConfigValue(Stream* stream, const char* args, bool isPitch) {
    uint8_t axisIndex = 0;
    float value = 0.0f;
    if (!parseAxisAndValue(args, axisIndex, value)) {
        stream->println(isPitch ? F("Usage: SET_PITCH <axis> <mm>   e.g. SET_PITCH 2 5")
                                : F("Usage: SET_RANGE <axis> <mm>   e.g. SET_RANGE 1 60"));
        return;
    }

    bool ok = isPitch ? AxisConfig::savePitch(axisIndex, value) : AxisConfig::saveRange(axisIndex, value);
    if (!ok) {
        stream->print(F("Invalid value, allowed: "));
        stream->print(isPitch ? AXIS_CONFIG_MIN_PITCH_MM : AXIS_CONFIG_MIN_RANGE_MM, 1);
        stream->print(F(" - "));
        stream->print(isPitch ? AXIS_CONFIG_MAX_PITCH_MM : AXIS_CONFIG_MAX_RANGE_MM, 1);
        stream->println(F(" mm"));
        return;
    }

    stream->print(F("Axis "));
    stream->print(axisIndex + 1);
    stream->print(isPitch ? F(": spindle pitch ") : F(": working range "));
    stream->print(value, 2);
    stream->println(F(" mm saved. Restart the controller to apply."));
    if (axisIndex >= numAxes) {
        stream->println(F("Note: this actuator is not enabled (NUM_ACTUATORS in Config.h)"));
    }
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
    } else if (strcasecmp(cmd, "DUMP_SERVO") == 0 || strcasecmp(cmd, "DUMP_SERVO 1") == 0 ||
               strcasecmp(cmd, "DUMP") == 0 || strcasecmp(cmd, "DUMP 1") == 0 ||
               strcasecmp(cmd, "DIFF_SERVO") == 0 || strcasecmp(cmd, "DIFF_SERVO 1") == 0 ||
               strcasecmp(cmd, "DIFF") == 0 || strcasecmp(cmd, "DIFF 1") == 0) {
        bool onlyDiff = (strncasecmp(cmd, "DIFF", 4) == 0);
        if (numAxes > 0 && axisUnits[0] != nullptr) {
            axisUnits[0]->dumpServoParameters(onlyDiff, stream);
        }
    } else if (strcasecmp(cmd, "DUMP_SERVO 2") == 0 || strcasecmp(cmd, "DUMP 2") == 0 ||
               strcasecmp(cmd, "DIFF_SERVO 2") == 0 || strcasecmp(cmd, "DIFF 2") == 0) {
        bool onlyDiff = (strncasecmp(cmd, "DIFF", 4) == 0);
        if (numAxes > 1 && axisUnits[1] != nullptr) {
            axisUnits[1]->dumpServoParameters(onlyDiff, stream);
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
    } else if (strcasecmp(cmd, "CONFIG") == 0) {
        printAxisConfig(stream);
        stream->println(F("Change (saved in flash, applied after restart):"));
        stream->println(F("  SET_PITCH <axis> <mm>  - spindle pitch, e.g. SET_PITCH 1 5"));
        stream->println(F("  SET_RANGE <axis> <mm>  - working range, e.g. SET_RANGE 1 60"));
        stream->println(F("  RESET_CONFIG           - delete saved values, use Config.h defaults"));
        stream->println(F("More settings: SETTINGS"));
    } else if (strncasecmp(cmd, "SET_PITCH ", 10) == 0) {
        setAxisConfigValue(stream, cmd + 10, true);
    } else if (strncasecmp(cmd, "SET_RANGE ", 10) == 0) {
        setAxisConfigValue(stream, cmd + 10, false);
    } else if (strncasecmp(cmd, "SERVO_WRITE ", 12) == 0) {
        servoWrite(stream, cmd + 12);
    } else if (strncasecmp(cmd, "SERVO_SAVE", 10) == 0) {
        servoSave(stream, cmd + 10);
    } else if (strcasecmp(cmd, "SETTINGS") == 0) {
        ControllerConfig::print(stream);
    } else if (strncasecmp(cmd, "SET ", 4) == 0) {
        ControllerConfig::set(stream, cmd + 4);
    } else if (strcasecmp(cmd, "RESET_CONFIG") == 0) {
        AxisConfig::reset();
        ControllerConfig::reset();
        stream->println(F("Saved configuration deleted, Config.h defaults apply after restart"));
        printAxisConfig(stream);
    } else if (strcasecmp(cmd, "RESTART") == 0 || strcasecmp(cmd, "REBOOT") == 0) {
        stream->println(F(">>> Restarting controller <<<"));
        stream->flush();
        for (uint8_t i = 0; i < numAxes; i++) {
            if (axisUnits[i] != nullptr) {
                axisUnits[i]->disableMotor();
            }
        }
        delay(200);
        ESP.restart();
    } else if (strcasecmp(cmd, "WEBGRAPHS") == 0) {
        webGraphs.printStatus(stream);
    } else if (strcasecmp(cmd, "HELP") == 0) {
        stream->println(F("Available Serial Commands:"));
        stream->println(F("  FLASH_SERVO    - Flash all 305 tuned parameters into iSV57 EEPROM/NVM"));
        stream->println(F("  DUMP_SERVO     - List all 305 servo parameters (read-only)"));
        stream->println(F("  DIFF_SERVO     - List servo parameters differing from the tuned table (read-only)"));
        stream->println(F("  ENABLE_SERVO   - Enable servo axis"));
        stream->println(F("  DISABLE_SERVO  - Disable servo axis"));
        stream->println(F("  HOME           - Re-run sensorless homing calibration"));
        stream->println(F("  STATUS         - Print live sensor load & voltage"));
        stream->println(F("  CONFIG         - Show spindle pitch & working range per axis"));
        stream->println(F("  SET_PITCH a mm - Save spindle pitch of axis a (1/2) in flash, e.g. SET_PITCH 2 5"));
        stream->println(F("  SET_RANGE a mm - Save working range of axis a (1/2) in flash, e.g. SET_RANGE 1 60"));
        stream->println(F("  SERVO_WRITE s reg value - Write one servo register (RAM), e.g. SERVO_WRITE 1 0x0008 3200"));
        stream->println(F("  SERVO_SAVE s   - Store the current servo registers in the servo EEPROM"));
        stream->println(F("  SETTINGS       - Show direction, position, idle & homing settings"));
        stream->println(F("  SET key value  - Save a setting in flash, e.g. SET IDLE_POSITION 15"));
        stream->println(F("  RESET_CONFIG   - Delete saved values, use Config.h defaults"));
        stream->println(F("  RESTART        - Restart the controller (applies saved pitch/range)"));
        stream->println(F("  WEBGRAPHS      - Print address of the live graph web page"));
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
