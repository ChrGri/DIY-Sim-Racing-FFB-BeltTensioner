# DIY Belt Tensioner - SimHub Protokoll- & Befehlsspezifikation

Dieses Dokument beschreibt alle Ein- und Ausgaben des seriellen Kommunikationsprotokolls zwischen dem **SimHub DIY Belt Tensioner Plugin** und dem Controller (ESP32 / Arduino), basierend auf der Original-Implementierung in `BeltTensionner.ino` und `AxisDriver.h`.

---

## 1. Serielle Schnittstellenparameter

| Parameter | Wert |
| :--- | :--- |
| **Baudrate** | `250000` bps |
| **Datenbits** | `8` |
| **Parität** | Keine (`N`) |
| **Stoppbits** | `1` |
| **Flusssteuerung** | Keine |
| **Zeilenendzeichen (ASCII)** | `\r\n` (`0x0D 0x0A` bzw. CR+LF) |

---

## 2. Paket-Framing (Binäres Protokoll)

Alle Steuerbefehle von SimHub an den Controller verwenden einen festen Rahmen:

```text
+---------------+---------------+---------------+----------------------+---------------+---------------+
| Header Byte 1 | Header Byte 2 |  Command ID   |     Payload Data     | Term Byte 1   | Term Byte 2   |
|     0xFF      |     0xFF      |  (1 Byte)     |    (0 - 8 Bytes)     |     0x0A      |     0x0D      |
+---------------+---------------+---------------+----------------------+---------------+---------------+
```

* **Header:** Immer `0xFF 0xFF` (2 Bytes)
* **Command ID:** `uint8_t` Befehlscode
* **Payload:** Abhängig vom Befehl (Big-Endian / MSB first)
* **Terminator:** Immer `0x0A 0x0D` (`\n\r` / 2 Bytes)

---

## 3. Eingabebefehle (SimHub $\rightarrow$ Controller)

### CMD 1 (`0x01`): Zielposition setzen (Set Target Position)
Überträgt die 16-Bit-Sollpositionen für bis zu 2 Motoren (Big-Endian).

* **Paketformat (7 Bytes):**
  ```text
  [0xFF] [0xFF] [0x01] [M1_High] [M1_Low] [M2_High] [M2_Low] [0x0A] [0x0D]
  ```
* **Wertebereich:** `0` bis `65535` (Mittelstellung = ca. 32767).
* **Umrechnung im Controller:**
  $$\text{TargetStep} = \text{constrain}\left(\frac{\text{InputValue} \times \text{TotalWorkingRange}}{65535}, 0, \text{TotalWorkingRange}\right)$$
* **Verhalten:**
  - Weckt den Motor bei Inaktivität auf (`EnableMotor`).
  - Fährt die berechnete Step-Position über `FastAccelStepper::moveTo()` an.
  - Setzt den Inaktivitäts-Timer zurück.
* **Antwort:** Keine direkte Datenantwort.

---

### CMD 2 (`0x02`): Maximalgeschwindigkeit setzen (Set Max Speed)
Konfiguriert die maximale Schrittfrequenz in Hz (Steps pro Sekunde) für beide Achsen.

* **Paketformat (7 Bytes):**
  ```text
  [0xFF] [0xFF] [0x02] [M1_High] [M1_Low] [M2_High] [M2_Low] [0x0A] [0x0D]
  ```
* **Wertebereich:** `uint16_t` (`1` bis `65535` Steps/s).
* **Verhalten:** Setzt `stepper->setSpeedInHz()` und wendet `applySpeedAcceleration()` an.
* **Antwort / Log:**
  ```text
  M1 Speed set to <speed>
  M2 Speed set to <speed>
  ```

---

### CMD 3 (`0x03`): Maximalbeschleunigung setzen (Set Max Acceleration)
Konfiguriert die maximale Beschleunigung in Steps/$s^2$ für beide Achsen (als 32-Bit Unsigned Integer).

* **Paketformat (11 Bytes):**
  ```text
  [0xFF] [0xFF] [0x03] [M1_B3] [M1_B2] [M1_B1] [M1_B0] [M2_B3] [M2_B2] [M2_B1] [M2_B0] [0x0A] [0x0D]
  ```
* **Wertebereich:** `uint32_t` (`1` bis `4294967295` Steps/$s^2$).
* **Verhalten:** Setzt `stepper->setAcceleration()` und wendet `applySpeedAcceleration()` an.
* **Antwort / Log:**
  ```text
  M1 Acceleration set to <accel>
  M2 Acceleration set to <accel>
  ```

---

### CMD 10 (`0x0A`): Anzahl aktiver Motoren abfragen (Query Enabled Motors)
Handshake-Befehl von SimHub zur Ermittlung der konfigurierten Achsenanzahl.

* **Paketformat (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0A] [0x0A] [0x0D]
  ```
* **Antwort vom Controller (ASCII-String mit CR+LF):**
  ```text
  Enabled motors:<N>
  ```
  *(Beispiel: `Enabled motors:1`)*

---

### CMD 11 (`0x0B`): Sensor-Diagnosedaten abfragen (Dump Sensor Diagnostic)
Liest den Live-Messwert des Endschalters / Sensors der angegebenen Achse aus.

* **Paketformat (6 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0B] [Axis_ID] [0x0A] [0x0D]
  ```
  *(wobei `Axis_ID` 0-basiert ist: `0` für Motor 1, `1` für Motor 2)*
* **Antwort vom Controller (ASCII-String mit CR+LF):**
  ```text
  Sensor #<Axis>:<Value>:Trigger level:<Level>:Triggered:<0|1>
  ```
  *(Beispiel: `Sensor #0:45:Trigger level:100:Triggered:0`)*

---

### CMD 12 (`0x0C`): Parkposition / Gurt entspannen (Park Now)
Triggert das sofortige Entspannen des Gurts und Fahren in die Park-/Leerlaufposition (10% bzw. ZeroOffset).

* **Paketformat (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0C] [0x0A] [0x0D]
  ```
* **Verhalten:** Setzt den Aktivitätszeitstempel auf `0`, wodurch die Motoren sofort in die Idle-Position fahren und nach Stillstand stromsparend deaktiviert werden.
* **Antwort:** Keine.

---

### CMD 13 (`0x0D`): Rekalibrierung anfordern (Discard Calibration)
Verwirft die aktuelle Nullpunkt-Kalibrierung und erzwingt eine erneute Homing-Fahrt.

* **Paketformat (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0D] [0x0A] [0x0D]
  ```
* **Verhalten:** Setzt `motorReady = false`, startet die Homing-Routine (MIN/MAX-Suche) und kalibriert den Nullpunkt neu.
* **Antwort / Log:**
  ```text
  M1 Starting stepper calibration
  ```

---

### CMD 14 (`0x0E`): Firmware-Version abfragen (Query Firmware Version)
Wird von SimHub direkt beim Verbindungsaufbau gesendet, um die Kompatibilität zu prüfen.

* **Paketformat (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0E] [0x0A] [0x0D]
  ```
* **Antwort vom Controller (ASCII-String mit CR+LF):**
  ```text
  2.0
  ```
  *(Wichtig: SimHub führt ein `new Version(response)` aus. Es dürfen vor oder hinter `"2.0"` keine anderen Zeichen oder Logzeilen stehen!)*

---

### CMD 15 (`0x0F`): Getunte Parameter in Servo-EEPROM flashen (Flash Tuned Parameters)
Flasht alle **305 getunten Register** aus `isv57_tunedParameters.h` (Pr0.00 bis Pr7.49) in 10er-Bursts bzw. Einzel-Verifizierungen auf den iSV57 Servo und speichert sie dauerhaft im internen NVM/EEPROM (`0x019A = 0x5555`).

* **Paketformat (5 Bytes):**
  ```text
  [0xFF] [0xFF] [0x0F] [0x0A] [0x0D]
  ```

---

## 4. Serielle ASCII-Befehle (Serieller Monitor)

Neben dem binären SimHub-Protokoll können im Seriellen Monitor (PlatformIO / Arduino IDE bei **250.000 Baud**) folgende Textkommandos eingegeben werden:

| ASCII-Kommando | Alias | Funktion / Beschreibung |
| :--- | :--- | :--- |
| **`FLASH_SERVO`** | `FLASH`, `FLASH 1`, `FLASH_SERVO 1` | Prüft und flasht alle **305 Parameter** aus `isv57_tunedParameters.h` in den Servo 1 und brennt sie ins interne EEPROM (`0x5555`). |
| **`FLASH_SERVO 2`** | `FLASH 2` | Flasht alle 305 Parameter auf Servo 2 (bei Dual-Actuator-Setup). |
| **`ENABLE_SERVO`** | `ENABLE`, `ENABLE 1` | Aktiviert die Servo-Endstufe (`0x0085 = 0x0383` & `0x0139 = 0x0008`) und bestromt den Motor sofort. |
| **`ENABLE_SERVO 2`** | `ENABLE 2` | Aktiviert Servo 2. |
| **`DISABLE_SERVO`** | `DISABLE` | Schaltet den Servo softwareseitig komplett stromlos (`0x0085 = 0x0303` & `0x0139 = 0x0000`). Welle ist frei drehbar. |
| **`HOME`** | `CALIBRATE` | Startet die automatische Homing- & Anschlagskalibrierung (aktiviert den Motor vorher automatisch). |
| **`STATUS`** | - | Gibt den aktuellen Laststrom (%), Busspannung (V) und Kalibrierstatus im SimHub-Format aus. |
| **`HELP`** | - | Zeigt eine Übersicht aller verfügbaren seriellen Befehle an. |

---

## 5. Ausgabemeldungen (Controller $\rightarrow$ SimHub / PC)

SimHub filtert alle vom Controller gesendeten Textzeilen nach folgenden Regeln:

### 1. Boot-Greeting (beim Start des Controllers)
Wird einmalig in `setup()` ausgegeben:
```text
<N> steppers enabled
```
*(z. B. `1 steppers enabled`)*

### 2. Status- & Diagnosemeldungen (SimHub Log-Format)
Jede Textnachricht, die für das SimHub-Logfenster bestimmt ist, **muss** mit `M1 ` oder `M2 ` (Präfix + Leerzeichen) beginnen:

| Log-Ausgabe | Bedeutung |
| :--- | :--- |
| `M1 Enabling motor` | Motor wird bestromt und aktiviert |
| `M1 Disabling motor` | Motor wird nach Inaktivität deaktiviert |
| `M1 Starting stepper calibration` | Kalibrierungs- / Homingroutine gestartet |
| `M1 Initial move finished` | Erste Freifahrt abgeschlossen |
| `M1 Starting calibration` | Suchfahrt Richtung Endanschlag / Sensor |
| `M1 Sensor triggered` | Endanschlag / Sensor-Trigger erkannt |
| `M1 Calibration successful.` | Homing erfolgreich abgeschlossen, Nullpunkt gesetzt |
| `M1 Speed set to <speed>` | Neue Geschwindigkeit übernommen |
| `M1 Acceleration set to <accel>` | Neue Beschleunigung übernommen |
| `M1 Sensor calibration completed` | Achse betriebsbereit |

---

## 6. Spezielle Betriebsmodi

### Sensor-Testmodus (`sensorTestMode = true`)
Wird in der Konfiguration `sensorTestMode = true` gesetzt, schaltet die Firmware in einen kontinuierlichen Diagnosemodus:
- Motoren bleiben permanent stromlos / deaktiviert.
- Der Controller gibt alle 500 ms fortlaufend Zeilen im Format `Sensor #<axis>:<value>:Trigger level:<level>:Triggered:<0|1>` aus.
- Nützlich zum Einmessen von Hall-Sensoren oder Dehnungsmessstreifen im Seriellen Monitor.

### Inaktivitäts-Watchdog (`idleDelay = 5000 ms`)
- Werden für mehr als `5000 ms` keine Bewegungsbefehle empfangen, fährt der Schlitten automatisch auf die 10%-Parkposition.
- Nach Stillstand werden die Spulen bestromungslos bzw. in den Haltemodus geschaltet (`Disabling motor`).
