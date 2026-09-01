# 📦 Bill of Materials (BOM) — DIY Active FFB Belt Tensioner

This Bill of Materials (BOM) provides the complete list of electrical and mechanical components required to build the **Active Force Feedback (FFB) Seatbelt Tensioner**. 

Most core components are intentionally shared with the [DIY Sim Racing FFB Pedal Ecosystem](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal-Mechanical-Design/tree/main/BOM).

> [!NOTE]
> **Simpler than a pedal!** Unlike an active FFB pedal, the belt tensioner does **not** need a load cell or an ADS1220 ADC amplifier. Physical homing, travel limits, and stall protection are handled sensorlessly through real-time Modbus current and encoder telemetry.

---

> ℹ️ **Affiliate Disclosure:**  
> Some links in this document are affiliate links (marked with an asterisk `*` or direct affiliate tags). When you purchase through these links, a small commission may be paid to help support open-source development and hardware prototyping—at no additional cost to you. Thank you for your support!

---

## ⚡ 1. Electronics & Controller

| Component | Description / Specification | Qty | Primary Link | Secondary Link |
| :--- | :--- | :---: | :--- | :--- |
| **Control PCB** | `ControlBoard_V6` (ESP32-S3 DevKit) or `ControlBoard_V7` (ESP32-S3 Zero) | 1 | [JLCPCB](https://jlcpcb.com/) | [Pedal Repo PCB Files](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal) |
| **ESP32-S3** | ESP32-S3 DevKitC-1 (for V6) or Waveshare ESP32-S3 Zero (for V7) | 1 | [Amazon*](https://amzn.to/4pjNnjq) | [AliExpress*](https://s.click.aliexpress.com/e/_c3IpHQ4P) |
| **Integrated Servo Motor** | StepperOnline iSV57T-130S (130W, short shaft preferred) or iSV57T-180S (180W) | 1 | [StepperOnline (130S)*](https://www.omc-stepperonline.com/de/nema-23-integrierter-easy-servo-motor-130w-3000rpm-0-45nm-63-73oz-in-20-50vdc-buerstenloser-dc-servomotor-kurze-welle-isv57t-130s?tracking=6721c5865911c) | [Hardware Kit*](https://www.omc-stepperonline.com/de/hardware-paket-fuer-diy-aktivpedal-von-chris-empfohlen-von-l-atelier-mit-isv57t-130s-lkn60-23dl060-060-st-rc07-act-pdl?tracking=6721c5865911c) |
| **RS232 Transceiver** | SP2323 / SP3232 / MAX3232 (20×16 or SOP-16) for Modbus telemetry | 1 | [Amazon*](https://amzn.to/4e1xEBK) | [AliExpress*](https://s.click.aliexpress.com/e/_c3BQLhi9) |
| **Schottky Diode** | SR5100 Schottky diode (reverse-polarity & inductive kickback protection) | 1 | [Amazon*](https://amzn.to/3CmN8jR) | [AliExpress*](https://s.click.aliexpress.com/e/_DdrrsrJ) |
| **DC Power Supply (PSU)** | 36V or 48V DC Power Supply (e.g. MeanWell LRS-350-36, ~9.7A) | 1 | [StepperOnline*](https://www.omc-stepperonline.com/de/lrs-350-36-mean-well-350w-36vdc-9-7a-115-230vac-geschlossenes-schaltnetzteil-lrs-350-36?tracking=6721c5865911c) | [MeanWell Official](https://www.meanwell.com/) |
| **Servo Debug Port Cable** | 5-Pin female connector for iSV57 tuning/Modbus port | 1 | [Amazon*](https://amzn.to/4uXvVTZ) | [AliExpress*](https://s.click.aliexpress.com/e/_oCbioHZ) |
| **XT30 Power Connector** | Male angled XT30 connector (board DC power input) | 1 | [Amazon*](https://amzn.to/4xkNmQ4) | [AliExpress*](https://s.click.aliexpress.com/e/_c4a68ig9) |
| **Screw Terminals** | 2.54mm pitch screw terminals (2-pin and/or 3-pin) | 1 | [Amazon*](https://amzn.eu/d/5S0YVBn) | [AliExpress*](https://s.click.aliexpress.com/e/_c3qY4kDr) |
| **Power Wire (AWG 18)** | Silicone insulated wire for high-current motor supply | ~0.5 m | [Amazon*](https://amzn.to/4htGLtR) | [AliExpress*](https://s.click.aliexpress.com/e/_oke6LFx) |
| **Signal Wire (AWG 30)** | Thin flexible wire for logic, RS232 signals, and status LED | ~0.5 m | [Amazon*](https://amzn.to/4geYNz0) | [AliExpress*](https://s.click.aliexpress.com/e/_olWGFI1) |

### 🛠️ Control PCB Assembly & Soldering Guide (V7 / V6)

A dedicated, step-by-step soldering and wiring guide for the belt tensioner is available here:  
👉 **[Control PCB Assembly & Soldering Guide](PCB_Assembly.md)** *(based on the original [Pedal Soldering_V7 Guide](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal-Mechanical-Design/tree/main/Soldering_V7))*

For the **Active Belt Tensioner**, assembly is substantially faster and simpler than for a pedal:
* ⚡ **Power Circuit:** Only solder the **SR5100 Schottky diode** and the **XT30 connector**. The MOSFET (FR120N) and brake resistor / brake chopper circuit are **not required**.
* 🚫 **No ADS1220 & No RC Filter:** Skip the ADS1220 chip and RC filter components completely (the belt tensioner does not use a load cell).
* 🔌 **Required Servo Interfaces:** You only need to wire:
  1. **RS232 Transceiver (SP2323 / SP3232):** For live Modbus telemetry, stall detection, and sensorless homing.
  2. **Pulse & Direction & Alarm Interface:** `PUL+` / `DIR+` / `ALM` and ground lines to the iSV57 integrated servo.

---

## 🔩 2. Mechanics, 3D Prints & Rig Mounting

| Component | Description / Specification | Qty | Primary Link | Secondary Link |
| :--- | :--- | :---: | :--- | :--- |
| **KK60 Linear Rail** | KK60 series linear module with ballscrew (10mm pitch, 60–150mm stroke) | 1 | [StepperOnline (LKN60)*](https://www.omc-stepperonline.com/de/lkn60-kk-serie-kugelgewindetrieb-linearmodul-maximale-horizontale-vertikale-nutzlast-30kg-10kg-hub-60mm-lkn60-23dl050-060?tracking=6721c5865911c) | [JLCMC JKK60](https://jlcmc.com/product/s/B16/BQD-JKK60/steel-linear-module-kk60-series#selection-tab) |
| **8mm to 8mm Shaft Coupler** | Flexible jaw/beam coupler (D25 L30) connecting servo shaft (8mm) to ballscrew (8mm) | 1 | [Amazon*](https://amzn.to/4qbL1CH) | [AliExpress*](https://s.click.aliexpress.com/e/_c3kPwW8h) |
| **3060 Adapter Plate** | 3D printed plate adapting KK60 rail hole pattern to 3060 extrusion | 1 | [STL Model (JKK60_to_3060_adapter.stl)](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal-Mechanical-Design/blob/main/MechanicalDesign_11_20215/Build/STL/3060_adapter/JKK60_to_3060_adapter.stl) | - |
| **Lower Load Cell Arm Adapter** | 3D printed carriage sled adapter for belt strap anchor | 1 | [STL Model (8mmLowerAdapter_v3.stl)](https://github.com/ChrGri/DIY-Sim-Racing-FFB-Pedal-Mechanical-Design/blob/main/MechanicalDesign_11_20215/Build/STL/LowerLoadcellArmAdapter/8mmLowerAdapter_v3.stl) | - |
| **3060 Aluminum Extrusion (400 mm)** | 30×60 mm T-slot profile (400 mm length) as rigid tensioner spine / rig mount | 1 | [Amazon*](https://amzn.to/4aPx3Sj) | [AliExpress*](https://s.click.aliexpress.com/e/_c39XbifP) |

---

## 🎗️ 3. Harness Straps & Soft Loops

| Component | Description / Specification | Qty | Primary Link | Secondary Link |
| :--- | :--- | :---: | :--- | :--- |
| **Soft Tie-Down Loops (20 cm)** | Heavy-duty 20 cm soft loops for harness attachment through sled adapter | 2–4 | [Amazon*](https://amzn.to/46AaihW) | [AliExpress*](https://s.click.aliexpress.com/e/_c2QzvU9v) |
| **Sim Racing Harness** | 4-Point or 5-Point racing harness (shoulder straps connect to tensioner loops) | 1 | [AliExpress*](https://s.click.aliexpress.com/e/_c3DNDQm7) | Standard 2" / 3" harness |

---

## 🔩 4. Fasteners & Screws

| Fastener | Description / Specification | Qty | Primary Link | Secondary Link |
| :--- | :--- | :---: | :--- | :--- |
| **M4 × 16 mm Screws** | Cylinder head socket cap screw (DIN 912), attaches KK60 linear rail & motor bracket | 4 | [Amazon*](https://amzn.to/4q5GSQA) | [AliExpress*](https://s.click.aliexpress.com/e/_c3bxyxud) |
| **M5 × 20 mm Screws** | Cylinder head socket cap screw (DIN 912), mounts 3060 adapter plate to 3060 profile | 4 | [Amazon*](https://amzn.to/4j8jHD6) | [AliExpress*](https://s.click.aliexpress.com/e/_c41gbyuD) |
| **M5 × 25 mm Screws** | Cylinder head socket cap screw (DIN 912), secures lower arm adapter to carriage sled | 4 | [Amazon*](https://amzn.to/4s0sBpY) | [AliExpress*](https://s.click.aliexpress.com/e/_c41gbyuD) |
| **M8 × 45 mm Threaded Rod** | Threaded rod / pin inside lower arm adapter for tie-down loop | 2 | [Amazon*](https://amzn.to/48Iyq48) | [AliExpress*](https://s.click.aliexpress.com/e/_c35xCCtj) |
| **3030 M5 Spring Ball Nut** | Roll-in spring ball T-slot nuts (for 8mm slot, 30-series aluminum profile) | 4 | [Amazon*](https://amzn.to/4qffsbd) | [AliExpress*](https://s.click.aliexpress.com/e/_c38nyFEZ) |

---

## 🛠️ Assembly & Sourcing Tips

1. **Servo Shaft Selection:** If ordering the iSV57T-130 or 180, make sure to pick the short shaft (`130S` / `180S`) version if available. If only the standard long shaft is available, you can trim the shaft with a metal handsaw or angle grinder.
2. **Coupler Alignment:** Always leave a 0.5–1.0 mm gap inside the flexible coupler between the motor shaft and ballscrew to prevent axial preload on the motor bearings.
3. **Rigid Mounting:** Because the active belt tensioner can pull with significant force and rapid acceleration (**600,000 steps/s²**), securely bolt the 3060 spine directly behind your seat using 30-series or 40-series gussets.
4. **Soldering Tools:** A good iron makes board assembly much easier. Recommended models include the [TS80*](https://s.click.aliexpress.com/e/_c33CYe8n) and [TS101*](https://s.click.aliexpress.com/e/_opJpOtW) (see [PCB Assembly Guide](PCB_Assembly.md) for soldering tutorial and instructions).
