#pragma once

#include <Arduino.h>

#define MODBUS_COIL_REGISTER_U8            0x01
#define MODBUS_DISCRET_REGISTER_U8         0x02
#define MODBUS_HOLDING_REGISTER_U8         0x03
#define MODBUS_INPUT_REGISTER_U8           0x04
#define MODBUS_WRITE_HOLDING_REGISTER_U8   0x06
#define MODBUS_WRITE_MULTIPLE_REGISTERS_U8 0x10

class Modbus {
private:
    bool logEnabled_b = false;
    uint32_t timeout_u32 = 100;
    HardwareSerial* serial_pHS = nullptr;
    uint8_t rawRxBuffer_au8[512];
    int32_t rawRxBufferLength_i32 = 0;
    uint8_t dataRxBuffer_au8[512];
    int32_t dataRxBufferLength_i32 = 0;
    int32_t defaultSlaveId_i32 = 0x3F; // 63
    uint8_t txBuffer_au8[64] = {0};

    int32_t computeCrc(uint8_t *buffer_pu8, int32_t bufferLength_i32);

public:
    Modbus();
    explicit Modbus(HardwareSerial* serial);

    void setSerial(HardwareSerial* serial);
    bool initialize(int32_t baudrate, int8_t rxPin, int8_t txPin, bool enableLogging_b = false);
    void setSerialTimeout(uint16_t timeout_u16);

    uint8_t readByteFromRxBuffer(int32_t index_i32);
    int32_t readBlockFromRxBuffer(int32_t index_i32);

    int32_t readHoldingRegisterFromDevice(int32_t slaveId_i32, int32_t registerAddress_i32, int32_t block_i32 = 1);
    int32_t writeHoldingRegisterToDevice(int32_t slaveId_i32, int32_t registerAddress_i32, uint16_t value_u16);
    int32_t writeHoldingRegistersToDevice(int32_t slaveId_i32, int32_t registerAddress_i32, const uint16_t* values_u16, uint8_t count_u8);

    bool writeAndVerifyDeviceParameter(uint16_t slaveId_u16, int16_t parameterAddress_i16, int32_t value_i32);
    void readDeviceParameter(uint16_t slaveId_u16, uint16_t parameterAddress_u16);

    void getRawRxBuffer(uint8_t *rawBuffer_pu8, uint8_t &rawBufferLength_u8);
    void getRawTxBuffer(uint8_t *rawBuffer_pu8, uint8_t &rawBufferLength_u8);

    int32_t sendRequestAndReceiveResponse(int32_t slaveId_i32, int32_t functionCode_i32, int32_t registerAddress_i32, int32_t numberOfRegisters_i32);

    int16_t convertRxBufferToInt16(int32_t index_i32);
};
