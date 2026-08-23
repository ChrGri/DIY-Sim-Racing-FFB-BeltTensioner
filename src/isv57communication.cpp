#include "isv57communication.h"
#include "isv57_tunedParameters.h"
#include <Arduino.h>

Isv57Communication::Isv57Communication() {
    this->slaveId = 63;
}

Isv57Communication::Isv57Communication(HardwareSerial* serial, int16_t defaultSlaveId) {
    this->slaveId = defaultSlaveId;
    this->modbus.setSerial(serial);
}

void Isv57Communication::setSerial(HardwareSerial* serial, int16_t defaultSlaveId) {
    this->slaveId = defaultSlaveId;
    this->modbus.setSerial(serial);
}

bool Isv57Communication::initialize(int32_t baudrate, int8_t rxPin, int8_t txPin) {
    this->modbus.setSerialTimeout(60);
    return this->modbus.initialize(baudrate, rxPin, txPin, false);
}

void Isv57Communication::setupServoStateReading() {
    modbus.writeAndVerifyDeviceParameter(slaveId, 0x0191, reg_add_position_given_p);
    modbus.writeAndVerifyDeviceParameter(slaveId, 0x0192, reg_add_velocity_current_feedback_percent);
    modbus.writeAndVerifyDeviceParameter(slaveId, 0x0193, reg_add_position_error_p);
    modbus.writeAndVerifyDeviceParameter(slaveId, 0x0194, reg_add_voltage_0p1V);
}

void Isv57Communication::sendTunedServoParameters(bool commandRotationDirection, uint32_t stepsPerMotorRev_u32) {
    modbus.writeAndVerifyDeviceParameter(slaveId, pr_0_00 + 6, commandRotationDirection ? 1 : 0);
    modbus.writeAndVerifyDeviceParameter(slaveId, pr_0_00 + 8, (long)stepsPerMotorRev_u32);
    modbus.writeAndVerifyDeviceParameter(slaveId, pr_1_00 + 37, 1052); // Alarm mask
}

void Isv57Communication::readServoStates() {
    for (uint8_t i = 0; i < NUMBER_OF_ISV57_REGISTERS_TO_READ_IN_CYCLIC_READ; i++) {
        regArray[i] = -1;
    }

    int8_t numberOfRegisters = NUMBER_OF_ISV57_REGISTERS_TO_READ_IN_CYCLIC_READ;
    int bytesReceived = modbus.sendRequestAndReceiveResponse(slaveId, 0x03, ref_cyclic_read_0, numberOfRegisters);

    dynamicStates.servo_receivedPacketIsValid_b = false;

    if (bytesReceived == (numberOfRegisters * 2)) {
        modbus.getRawRxBuffer(raw, len);
        for (uint8_t regIdx = 0; regIdx < numberOfRegisters; regIdx++) {
            regArray[regIdx] = modbus.convertRxBufferToInt16(regIdx);
        }

        dynamicStates.servo_pos_given_p = regArray[0];
        dynamicStates.servo_current_percent = regArray[1];
        dynamicStates.servo_pos_error_p = regArray[2];
        dynamicStates.servoVoltage0p1V_i16 = regArray[3];

        dynamicStates.lastUpdateTimeInMS_u32 = millis();
        dynamicStates.servo_cycleCounter_u32++;
        dynamicStates.servo_receivedPacketIsValid_b = (dynamicStates.servoVoltage0p1V_i16 >= 50);
    }
}

bool Isv57Communication::checkCommunication() {
    return (modbus.sendRequestAndReceiveResponse(slaveId, 0x03, 0x0000, 2) > 0);
}

bool Isv57Communication::findServosSlaveId() {
    if (checkCommunication()) {
        return true;
    }

    int commonIds[] = {63, 1, 2, 0, 3, 4, 5};
    for (int id : commonIds) {
        if (modbus.sendRequestAndReceiveResponse(id, 0x03, 0x0000, 2) > 0) {
            slaveId = (int16_t)id;
            return true;
        }
        delay(2);
    }

    return false;
}

bool Isv57Communication::clearServoAlarms() {
    return (modbus.writeHoldingRegisterToDevice(slaveId, 0x019A, 0x1111) > 0);
}

void Isv57Communication::disableAxis() {
    // Keep servo in hardware power state
}

void Isv57Communication::enableAxis() {
    // Keep servo in hardware power state
}

void Isv57Communication::clearServoUnitPosition() {
    modbus.writeAndVerifyDeviceParameter(slaveId, pr_5_00 + 20, 0);
    delay(20);
    modbus.writeAndVerifyDeviceParameter(slaveId, pr_5_00 + 20, 1);
    delay(20);
}

bool Isv57Communication::setServoVoltage(uint16_t voltageInVolt_u16) {
    return modbus.writeAndVerifyDeviceParameter(slaveId, pr_7_00 + 32, voltageInVolt_u16 + 2);
}

bool Isv57Communication::setPositionSmoothingFactor(uint16_t posSmoothingFactor_u16) {
    return modbus.writeAndVerifyDeviceParameter(slaveId, pr_2_00 + 22, posSmoothingFactor_u16);
}

void Isv57Communication::setZeroPos() {
    zeroPos = dynamicStates.servo_pos_given_p;
}

int Isv57Communication::readRegisters(uint16_t startAddr_u16, uint8_t count_u8, int16_t* out_pi16) {
    if (count_u8 == 0 || out_pi16 == nullptr) return -1;
    int bytes = modbus.sendRequestAndReceiveResponse(slaveId, 0x03, startAddr_u16, count_u8);
    if (bytes == count_u8 * 2) {
        for (uint8_t i = 0; i < count_u8; i++) {
            out_pi16[i] = modbus.convertRxBufferToInt16(i);
        }
        return count_u8;
    }
    return -1;
}

int32_t Isv57Communication::writeHoldingRegisterToDevice(int32_t slaveId_i32, int32_t registerAddress_i32, uint16_t value_u16) {
    return modbus.writeHoldingRegisterToDevice(slaveId_i32, registerAddress_i32, value_u16);
}

int32_t Isv57Communication::writeHoldingRegistersToDevice(int32_t slaveId_i32, int32_t registerAddress_i32, const uint16_t* values_u16, uint8_t count_u8) {
    return modbus.writeHoldingRegistersToDevice(slaveId_i32, registerAddress_i32, values_u16, count_u8);
}
