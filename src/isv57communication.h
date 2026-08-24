#pragma once

#include "Modbus.h"

#define NUMBER_OF_ISV57_REGISTERS_TO_READ_IN_CYCLIC_READ 4

// Servo state register addresses
#define reg_add_position_given_p              0x0001 // PositionGiven(p)
#define reg_add_position_feedback_p           0x0002 // PositionFeedback(p)
#define reg_add_position_error_p              0x0003 // PositionError(p)
#define reg_add_velocity_current_feedback_percent 0x0081 // CurrentFeedback(%)
#define reg_add_voltage_0p1V                  0x0140 // DCBusVoltage(0.1V)

// Cyclic read trigger registers
#define ref_cyclic_read_0                     0x01F3
#define ref_cyclic_read_1                     0x01F4
#define ref_cyclic_read_2                     0x01F5
#define ref_cyclic_read_3                     0x01F6

// Servo parameter group base addresses
#define pr_0_00 0x0000 // Pr0.xx
#define pr_1_00 0x0019 // Pr1.xx (0x0000 + 25)
#define pr_2_00 0x0041 // Pr2.xx (pr_1_00 + 40)
#define pr_4_00 0x007D // Pr4.xx (pr_3_00 + 30)
#define pr_5_00 0x00AF // Pr5.xx (pr_4_00 + 50)
#define pr_7_00 0x00FF // Pr7.xx (pr_6_00 + 40)

struct Isv57DynamicStates {
    int32_t servo_cycleCounter_u32 = 0;
    int16_t servo_pos_given_p = 0;
    int16_t servo_pos_error_p = 0;
    int16_t servo_current_percent = 0;
    int16_t servoVoltage0p1V_i16 = 0;
    unsigned long lastUpdateTimeInMS_u32 = 0;
    bool servo_receivedPacketIsValid_b = false;
};

class Isv57Communication {
public:
    int16_t slaveId = 63;
    Isv57DynamicStates dynamicStates;
    int16_t regArray[NUMBER_OF_ISV57_REGISTERS_TO_READ_IN_CYCLIC_READ] = {0};

    Isv57Communication();
    explicit Isv57Communication(HardwareSerial* serial, int16_t defaultSlaveId = 63);

    void setSerial(HardwareSerial* serial, int16_t defaultSlaveId = 63);
    bool initialize(int32_t baudrate, int8_t rxPin, int8_t txPin);

    void setupServoStateReading();
    void sendTunedServoParameters(bool commandRotationDirection, uint32_t stepsPerMotorRev_u32);
    bool flashTunedParameters(bool commandRotationDirection, uint32_t stepsPerMotorRev_u32, Stream* logStream = nullptr);
    void readServoStates();
    bool checkCommunication();
    bool findServosSlaveId();
    bool clearServoAlarms();
    void disableAxis();
    void enableAxis();
    void clearServoUnitPosition();
    bool setServoVoltage(uint16_t voltageInVolt_u16);
    bool setPositionSmoothingFactor(uint16_t posSmoothingFactor_u16);

    void setZeroPos();
    int16_t getZeroPos() const { return zeroPos; }
    int16_t getPosFromMin() const { return dynamicStates.servo_pos_given_p - zeroPos; }

    int readRegisters(uint16_t startAddr_u16, uint8_t count_u8, int16_t* out_pi16);
    int32_t writeHoldingRegisterToDevice(int32_t slaveId_i32, int32_t registerAddress_i32, uint16_t value_u16);
    int32_t writeHoldingRegistersToDevice(int32_t slaveId_i32, int32_t registerAddress_i32, const uint16_t* values_u16, uint8_t count_u8);

    Modbus* getModbus() { return &modbus; }

private:
    Modbus modbus;
    int16_t zeroPos = 0;
    uint8_t raw[128] = {0};
    uint8_t len = 0;
};
