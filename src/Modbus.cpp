#include "Modbus.h"
#include <Arduino.h>

// CRC-16-Modbus constants
static const int32_t g_crcPolynomial_i32 = 0xA001;
static const int32_t g_crcInitialValue_i32 = 0xFFFF;

Modbus::Modbus() {
    this->serial_pHS = nullptr;
}

Modbus::Modbus(HardwareSerial* serial) {
    this->serial_pHS = serial;
}

void Modbus::setSerial(HardwareSerial* serial) {
    this->serial_pHS = serial;
}

void Modbus::lock() {
    if (this->mutex != nullptr) {
        xSemaphoreTake(this->mutex, portMAX_DELAY);
    }
}

void Modbus::unlock() {
    if (this->mutex != nullptr) {
        xSemaphoreGive(this->mutex);
    }
}

bool Modbus::initialize(int32_t baudrate, int8_t rxPin, int8_t txPin, bool enableLogging_b) {
    this->logEnabled_b = enableLogging_b;
    if (this->mutex == nullptr) {
        this->mutex = xSemaphoreCreateMutex();
    }
    if (this->serial_pHS != nullptr) {
        if (rxPin >= 0 && txPin >= 0) {
            this->serial_pHS->begin(baudrate, SERIAL_8N1, rxPin, txPin);
        } else {
            this->serial_pHS->begin(baudrate, SERIAL_8N1);
        }
    }
    return true;
}

void Modbus::setSerialTimeout(uint16_t timeout_u16) {
    timeout_u32 = timeout_u16;
}

uint8_t Modbus::readByteFromRxBuffer(int32_t index_i32) {
    return rawRxBuffer_au8[index_i32 + 3];
}

int32_t Modbus::readBlockFromRxBuffer(int32_t index_i32) {
    return (((uint16_t)dataRxBuffer_au8[index_i32 * 2] << 8) | dataRxBuffer_au8[index_i32 * 2 + 1]);
}

void Modbus::readDeviceParameter(uint16_t slaveId_u16, uint16_t parameterAddress_u16) {
    if (this->serial_pHS == nullptr) return;

    uint8_t rawBuffer_au8[2];
    uint8_t length_u8 = 0;
    int16_t registerArray_ai16[4] = {-1, -1, -1, -1};

    if (sendRequestAndReceiveResponse(slaveId_u16, 0x03, parameterAddress_u16, 2) > 0) {
        getRawRxBuffer(rawBuffer_au8, length_u8);
        registerArray_ai16[0] = convertRxBufferToInt16(0);
    }
    
    int16_t returnValue_i16 = registerArray_ai16[0];

    if (logEnabled_b) {
        Serial.print("Parameter address: ");
        Serial.print(parameterAddress_u16);
        Serial.print(", actual: ");
        Serial.println(returnValue_i16);
    }

    delay(20);
}

bool Modbus::writeAndVerifyDeviceParameter(uint16_t slaveId_u16, int16_t parameterAddress_i16, int32_t value_i32) {
    if (this->serial_pHS == nullptr) return false;

    bool registerWritten_b = false;
    bool registerValueAsTarget_b = false;

    for (uint8_t tryIndex_u8 = 0; tryIndex_u8 < 3; tryIndex_u8++) {
        if (registerValueAsTarget_b) {
            break;
        }

        uint8_t rawBuffer_au8[2];
        uint8_t length_u8 = 0;
        int16_t registerArray_ai16[4] = {-1, -1, -1, -1};
        
        if (sendRequestAndReceiveResponse(slaveId_u16, 0x03, parameterAddress_i16, 2) > 0) {
            getRawRxBuffer(rawBuffer_au8, length_u8);
            registerArray_ai16[0] = convertRxBufferToInt16(0);
        }
        
        int16_t returnValue_i16 = registerArray_ai16[0];
        int32_t targetValue_i32 = value_i32;

        if (returnValue_i16 != targetValue_i32 && returnValue_i16 != -1) {
            delay(5);
            writeHoldingRegisterToDevice(slaveId_u16, parameterAddress_i16, targetValue_i32);
            registerWritten_b = true;
        } else if (returnValue_i16 == targetValue_i32) {
            registerValueAsTarget_b = true;
        }
        delay(2);
    }

    return registerWritten_b;
}

int32_t Modbus::readHoldingRegisterFromDevice(int32_t slaveId_i32, int32_t registerAddress_i32, int32_t block_i32) {
    if (block_i32 > 2) {
        block_i32 = 2;
    }

    if (sendRequestAndReceiveResponse(slaveId_i32, MODBUS_HOLDING_REGISTER_U8, registerAddress_i32, block_i32)) {
        if (block_i32 == 2) {
            uint32_t high_u32 = (uint32_t)readBlockFromRxBuffer(0);
            uint32_t low_u32 = (uint32_t)readBlockFromRxBuffer(1);
            return (int32_t)((high_u32 << 16) | low_u32);
        } else {
            return readBlockFromRxBuffer(0);
        }
    } else {
        return -1;
    }
}

int32_t Modbus::sendRequestAndReceiveResponse(int32_t slaveId_i32, int32_t functionCode_i32, int32_t registerAddress_i32, int32_t numberOfRegisters_i32) {
    if (this->serial_pHS == nullptr) return -1;

    lock();

    int32_t crc_i32;
    txBuffer_au8[0] = (uint8_t)slaveId_i32;
    txBuffer_au8[1] = (uint8_t)functionCode_i32;
    txBuffer_au8[2] = (uint8_t)(registerAddress_i32 >> 8);
    txBuffer_au8[3] = (uint8_t)(registerAddress_i32 & 0xFF);
    txBuffer_au8[4] = (uint8_t)(numberOfRegisters_i32 >> 8);
    txBuffer_au8[5] = (uint8_t)(numberOfRegisters_i32 & 0xFF);
    crc_i32 = this->computeCrc(txBuffer_au8, 6);
    txBuffer_au8[6] = (uint8_t)(crc_i32 & 0xFF);
    txBuffer_au8[7] = (uint8_t)((crc_i32 >> 8) & 0xFF);
 
    while (this->serial_pHS->available()) {
        this->serial_pHS->read();
    }

    this->serial_pHS->write(txBuffer_au8, 8);
    this->serial_pHS->flush();

    uint32_t startTime_u32 = millis();
    rawRxBufferLength_i32  = 0;
    dataRxBufferLength_i32 = 0;
    int32_t echoMatchCount_i32 = 0;
    int32_t receivedByte_i32;
    uint8_t receiveState_u8 = 0;

    bool allDataReceived_b = false;
    while (!allDataReceived_b && ((millis() - startTime_u32) < timeout_u32)) {
        delay(1);
       
        while (this->serial_pHS->available()) {
            receivedByte_i32 = this->serial_pHS->read();

            if (receiveState_u8 == 0) {
                if (txBuffer_au8[echoMatchCount_i32] == receivedByte_i32) {
                    echoMatchCount_i32++;
                } else {
                    echoMatchCount_i32 = 0;
                }
                if (echoMatchCount_i32 == 2) { 
                    receiveState_u8 = 1; 
                }
            } else if (receiveState_u8 == 1) {
                rawRxBuffer_au8[0] = txBuffer_au8[0];
                rawRxBuffer_au8[1] = txBuffer_au8[1];
                rawRxBuffer_au8[2] = (uint8_t)receivedByte_i32;
                rawRxBufferLength_i32 = 3;
                receiveState_u8 = 2;
            } else if (receiveState_u8 == 2) {
                this->rawRxBuffer_au8[rawRxBufferLength_i32++] = (uint8_t)receivedByte_i32;

                if (rawRxBufferLength_i32 >= rawRxBuffer_au8[2] + 5) { 
                    allDataReceived_b = true;
                    break; 
                }
            }
        }
    }

    int32_t result = -1;
    if (rawRxBufferLength_i32 > 2) {
        int32_t receivedCrc_i32 = ((uint16_t)rawRxBuffer_au8[rawRxBufferLength_i32 - 1] << 8) | rawRxBuffer_au8[rawRxBufferLength_i32 - 2];
        int32_t computedCrc_i32 = computeCrc(rawRxBuffer_au8, rawRxBufferLength_i32 - 2);

        if (receivedCrc_i32 == computedCrc_i32) {
            dataRxBufferLength_i32 = rawRxBuffer_au8[2];
            result = dataRxBufferLength_i32;
        }
    }

    unlock();
    return result;
}

int16_t Modbus::convertRxBufferToInt16(int32_t index_i32) {
    int32_t address_i32 = (index_i32 * 2) + 3;
    return (int16_t)(((uint16_t)rawRxBuffer_au8[address_i32] << 8) | rawRxBuffer_au8[address_i32 + 1]);
}

void Modbus::getRawRxBuffer(uint8_t *rawBuffer_pu8, uint8_t &rawBufferLength_u8) {
    rawBufferLength_u8 = this->rawRxBufferLength_i32;
    for (int32_t i = 0; i < this->rawRxBufferLength_i32; i++) {
        rawBuffer_pu8[i] = this->rawRxBuffer_au8[i];
    }
}

void Modbus::getRawTxBuffer(uint8_t *rawBuffer_pu8, uint8_t &rawBufferLength_u8) {
    rawBufferLength_u8 = 8;
    for (int32_t i = 0; i < 8; i++) {
        rawBuffer_pu8[i] = this->txBuffer_au8[i];
    }
}

int32_t Modbus::computeCrc(uint8_t *buffer_pu8, int32_t bufferLength_i32) {
    int32_t crc_i32 = g_crcInitialValue_i32;
    for (int32_t i = 0; i < bufferLength_i32; i++) {
        crc_i32 ^= (int32_t)buffer_pu8[i];
        for (int32_t j = 0; j < 8; j++) {
            if (crc_i32 & 0x0001) {
                crc_i32 = (crc_i32 >> 1) ^ g_crcPolynomial_i32;
            } else {
                crc_i32 = crc_i32 >> 1;
            }
        }
    }
    return crc_i32;  
}

int32_t Modbus::writeHoldingRegisterToDevice(int32_t slaveId_i32, int32_t registerAddress_i32, uint16_t value_u16) {
    if (this->serial_pHS == nullptr) return -1;

    lock();

    int32_t crc_i32;
    txBuffer_au8[0] = (uint8_t)slaveId_i32;
    txBuffer_au8[1] = MODBUS_WRITE_HOLDING_REGISTER_U8;
    txBuffer_au8[2] = (uint8_t)(registerAddress_i32 >> 8);
    txBuffer_au8[3] = (uint8_t)(registerAddress_i32 & 0xFF);
    txBuffer_au8[4] = (uint8_t)(value_u16 >> 8);
    txBuffer_au8[5] = (uint8_t)(value_u16 & 0xFF);
    crc_i32 = this->computeCrc(txBuffer_au8, 6);
    txBuffer_au8[6] = (uint8_t)(crc_i32 & 0xFF);
    txBuffer_au8[7] = (uint8_t)((crc_i32 >> 8) & 0xFF);
	
    while (this->serial_pHS->available()) {
        this->serial_pHS->read();
    }

    this->serial_pHS->write(txBuffer_au8, 8);
    this->serial_pHS->flush();

    uint32_t startTime_u32 = millis();
    int32_t echoMatchCount_i32 = 0;
    int32_t receivedByte_i32;
  
    bool responseReceived_b = false;
    while (((millis() - startTime_u32) < timeout_u32) && !responseReceived_b) {
        while (this->serial_pHS->available()) {
            receivedByte_i32 = this->serial_pHS->read();
            if (txBuffer_au8[echoMatchCount_i32] == receivedByte_i32) {
                echoMatchCount_i32++;
            } else {
                echoMatchCount_i32 = 0;
            }

            if (echoMatchCount_i32 == 8) {
                responseReceived_b = true;
                break;
            }
        }
        if (responseReceived_b) break;
        delay(1);
    }

    delay(5);
    unlock();
    return responseReceived_b ? 1 : -1;
}

int32_t Modbus::writeHoldingRegistersToDevice(int32_t slaveId_i32, int32_t registerAddress_i32, const uint16_t* values_u16, uint8_t count_u8) {
    if (this->serial_pHS == nullptr || count_u8 > 10) return -1;

    lock();

    uint8_t localTxBuffer[32];
    localTxBuffer[0] = (uint8_t)slaveId_i32;
    localTxBuffer[1] = MODBUS_WRITE_MULTIPLE_REGISTERS_U8;
    localTxBuffer[2] = (uint8_t)(registerAddress_i32 >> 8);
    localTxBuffer[3] = (uint8_t)(registerAddress_i32 & 0xFF);
    localTxBuffer[4] = 0;
    localTxBuffer[5] = count_u8;
    localTxBuffer[6] = count_u8 * 2;
    
    for (uint8_t i = 0; i < count_u8; i++) {
        localTxBuffer[7 + i * 2] = (uint8_t)(values_u16[i] >> 8);
        localTxBuffer[8 + i * 2] = (uint8_t)(values_u16[i] & 0xFF);
    }
    
    uint8_t length = 7 + count_u8 * 2;
    int32_t crc_i32 = this->computeCrc(localTxBuffer, length);
    localTxBuffer[length]     = (uint8_t)(crc_i32 & 0xFF);
    localTxBuffer[length + 1] = (uint8_t)((crc_i32 >> 8) & 0xFF);
    
    while (this->serial_pHS->available()) {
        this->serial_pHS->read();
    }

    this->serial_pHS->write(localTxBuffer, length + 2);
    this->serial_pHS->flush();

    uint32_t startTime_u32 = millis();
    uint8_t rxBuffer[8];
    uint8_t rxCount = 0;
    
    bool responseReceived_b = false;
    while (((millis() - startTime_u32) < timeout_u32) && !responseReceived_b) {
        while (this->serial_pHS->available()) {
            rxBuffer[rxCount++] = (uint8_t)this->serial_pHS->read();
            
            if (rxCount == 5 && rxBuffer[1] == 0x90) {
                responseReceived_b = false; 
                break;
            }
            
            if (rxCount == 8) {
                int32_t receivedCrc = ((uint16_t)rxBuffer[7] << 8) | rxBuffer[6];
                int32_t computedCrc = this->computeCrc(rxBuffer, 6);
                
                if (rxBuffer[0] == slaveId_i32 && rxBuffer[1] == 0x10 && receivedCrc == computedCrc) {
                    responseReceived_b = true;
                }
                break;
            }
        }
        if (responseReceived_b || rxCount == 8 || (rxCount == 5 && rxBuffer[1] == 0x90)) break; 
        delay(1);
    }
    delay(5);
    unlock();
    return responseReceived_b ? 1 : -1;
}
