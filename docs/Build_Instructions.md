# 🔧 Build Instructions — Dual Channel Belt Tensioner

Step-by-step assembly of the dual channel belt tensioner. Each step adds one group of parts. The pictures come from the CAD assembly ([`cad/DIY_BeltTensioner_Assembly.solidcraft`](../cad/DIY_BeltTensioner_Assembly.solidcraft)). Where parts are screwed into a profile, an exploded view shows the order: **profile → T-nut → printed part → screw**.

<p align="center">
  <img src="media/build/step12.png" alt="Finished dual channel belt tensioner" width="60%">
</p>

The full lists of printed and purchased parts, including all screw lengths, are in the **[Mechanical Design Guide](Mechanical_Design_Dual_Channel.md)**. All printed parts are in [`cad/stl/`](../cad/stl/); the quantity is part of each file name.

---

## 🧰 Before You Start

* **Print all parts first.** Print the hood segments front-face down, the ControlBoard holder on its flat back and the belt spacers standing on their collar. None of them need supports in that orientation.
* **Press in the M3 heat-set inserts** (for Ø 4.2 mm holes) before mounting anything. That is easier while the parts are loose:

  | Part | Inserts |
  | :--- | :---: |
  | Upper back plate (×2) | 7 each |
  | Lower back plate (×2) | 1 each |
  | ControlBoard holder | 4 |

* **T-nuts:** all printed parts are fixed to the profiles with M5 screws and M5 T-nuts (drop-in "hammer" nuts for 20-series slots). Drop a T-nut into the slot at the right height, put the part on, insert the screw and tighten it: the nut turns 90° and locks in the slot. 42 T-nuts are needed in total.
* **Tools:** hex keys (2.5 / 3 / 4 / 6 mm), soldering iron for the inserts.

> [!TIP]
> "Front" in these instructions is the side facing the driver's back, where the hood with the hexagonal vents sits. "Back" is the side facing the seat and rig.

---

## Step 1 — Frame

<img src="media/build/step01.png" alt="Step 1: frame" width="49%"> <img src="media/build/step01b.png" alt="Step 1: 4020 connector exploded view" width="49%">

| Parts | Qty |
| :--- | :---: |
| 2040 V-slot profile, 400 mm | 2 |
| `4020_connector` (printed) | 1 |
| 2020 V-slot profile, 400 mm | 1 |
| 2020 corner bracket | 2 |
| M5×12 + T-nut | 4 |
| M5×10 + T-nut | 4 |

1. Stack the two 2040 profiles end to end. The 40 mm side faces sideways.
2. Join them with the `4020_connector` on the front: one T-nut per hole in the front slots, four M5×12 screws (right picture).
3. Mount the 2020 crossbar on top of the spine with the two corner brackets, using M5×10 screws and T-nuts.

---

## Step 2 — Rig Mount

<img src="media/build/step02.png" alt="Step 2: 2040 holder with both rig adapters (rear view)" width="49%"> <img src="media/build/step02b.png" alt="Step 2: 2040 holder exploded view" width="49%">

| Parts | Qty |
| :--- | :---: |
| `2040Holder_BeltTensionerAdapter` (printed) | 1 |
| `2040Holder_RigAdapter` (printed, left and right) | 2 |
| M12 threaded rod (~150 mm) with nuts | 1 |
| M5×20 + T-nut | 4 |

1. Screw the `BeltTensionerAdapter` to the **back** of the lower 2040, about 100 mm above its lower end: four T-nuts in the back slots, four M5×20 screws.
2. Push the M12 rod through the adapter and fit one `RigAdapter` on each side. Both are the same part, turned to face each other.
3. Fix the spine to the rig with the M12 rod. Tighten it fully only after the rest of the frame is aligned.

---

## Step 3 — Build the Belt Rollers

<img src="media/build/step03r1.png" alt="Step 3: press the bearings into the roller" width="49%"> <img src="media/build/step03r2.png" alt="Step 3: M5x20 axle screws through the adapters into the bearings" width="49%">

| Parts | Qty |
| :--- | :---: |
| `Roller` (printed) | 2 |
| `RollerAdapter` (printed) | 4 |
| Ball bearing 625-2Z (5 × 16 × 5 mm) | 4 |
| M5×20 cylinder head screw | 4 |

Build both rollers on the bench:

1. **Bearings (left picture):** press one 625-2Z bearing into the seat at each end of the roller until it sits against the shoulder. A vice with soft jaws or a flat piece of wood helps.
2. **Adapters and axle screws (right picture):** hold one `RollerAdapter` against each end of the roller. Push an M5×20 screw from the outside through the counterbored hole of each adapter and screw it into the bearing's inner ring. The small collar on the adapter rests on the inner ring, so the roller can spin freely.
3. Tighten both screws until the adapters are firm, then check that the roller turns without rubbing.

---

## Step 4 — Mount the Belt Rollers

<img src="media/build/step03.png" alt="Step 4: belt rollers on the 2020" width="49%"> <img src="media/build/step03b.png" alt="Step 4: roller adapters exploded view" width="49%">

| Parts | Qty |
| :--- | :---: |
| Roller assemblies (step 3) | 2 |
| M5×16 + T-nut | 8 |

1. Each adapter is screwed to the 2020 twice: once into the **top** slot and once into the **front** slot (right picture). Drop the T-nuts in first, then set the roller assembly on and tighten the M5×16 screws.
2. Check that the rollers still spin freely.

---

## Step 5 — Seat Distance Holders

<img src="media/build/step04.png" alt="Step 5: seat distance holders" width="49%"> <img src="media/build/step04b.png" alt="Step 5: seat distance holder exploded view" width="49%">

| Parts | Qty |
| :--- | :---: |
| `SeatDistanceHolder` (printed) | 2 |
| M5×16 + T-nut | 2 |
| M8×100 cylinder head screw | 2 |
| M8 bolt damper | 2 |

1. Screw one `SeatDistanceHolder` to each end of the 2020 on the front: one T-nut in the front slot, one M5×16 screw.
2. Push an M8×100 screw through the lower hole from the front. It runs below the 2020 to the back, where you fit the bolt damper.
3. The dampers rest against the seat back. Adjust the screws so the frame sits square to the seat.

---

## Step 6 — Linear Modules on the Upper Back Plates

<img src="media/build/step05.png" alt="Step 6: upper back plates with JKK60 linear modules" width="49%"> <img src="media/build/step05b.png" alt="Step 6: upper back plate exploded view" width="49%">

| Parts | Qty |
| :--- | :---: |
| `Rueckplatte_oben_links` / `_rechts` (printed, inserts fitted) | 1 + 1 |
| JKK60 linear module, 10 mm pitch, 150 mm stroke | 2 |
| M5×16 + T-nut | 8 |

1. Screw one JKK60 module to each upper back plate with M4 screws. The rails face **forward**, and the modules are mirrored left and right.
2. Mount both plates to the **back** of the 2040: four T-nuts per plate in the back slots, four M5×16 screws each (right picture, module hidden). The modules then sit beside the spine at about ±110 mm.

---

## Step 7 — Servos

<img src="media/build/step06.png" alt="Step 7: iSV57 servos flanged to the modules" width="70%">

| Parts | Qty |
| :--- | :---: |
| StepperOnline iSV57T-130S | 2 |
| 8 mm to 8 mm shaft coupler | 2 |

1. Fit the coupler to the ballscrew and leave a 0.5–1 mm gap to the motor shaft inside the coupler.
2. Flange each servo to the lower end of its module.
3. Turn the servos so their **connectors point inwards**, towards the spine. The ControlBoards will sit there.

---

## Step 8 — Lower Back Plates

<img src="media/build/step07.png" alt="Step 8: lower back plates (rear view)" width="49%"> <img src="media/build/step07b.png" alt="Step 8: lower back plate exploded view" width="49%">

| Parts | Qty |
| :--- | :---: |
| `Rueckplatte_unten_links` / `_rechts` (printed, inserts fitted) | 1 + 1 |
| M5×12 + T-nut | 4 |

1. Mount both lower back plates to the **back** of the 2040, below the upper plates and between the servos: two T-nuts and two M5×12 screws per plate.
2. They rest on top of the 2040 holder. Each one carries the insert for the hood bottom.

---

## Step 9 — Power Supply

<img src="media/build/step08a.png" alt="Step 9a: adapters on the back of the power supply" width="49%"> <img src="media/build/step08b.png" alt="Step 9b: power supply onto the 2040, side screws" width="49%">

<img src="media/build/step08.png" alt="Step 9: power supply mounted" width="70%">

| Parts | Qty |
| :--- | :---: |
| MeanWell LRS-350 (36 V or 48 V variant) | 1 |
| `Lrs350To2040Adapter` (printed) | 2 |
| M4×10 | 4 |
| M5×12 + T-nut | 4 |

1. **Adapters to the power supply (picture 9a):** screw both adapters to the back of the power supply, two M4×10 screws each. The screw heads sit in the counterbores on the side that later faces the 2040, so do this first.
2. **Power supply onto the 2040 (picture 9b):** drop two T-nuts into each **side** slot of the upper 2040. Slide the power supply onto the front of the 2040 so the adapter legs straddle the profile, between the rails and at their upper end. The **terminal strip faces down**.
3. Fix each adapter leg from the side with an M5×12 screw into the T-nuts.

> [!CAUTION]
> The power supply is connected to mains voltage. Only wire it if you are qualified to do so, connect protective earth, and never work on it while it is plugged in.

---

## Step 10 — ControlBoards

<img src="media/build/step09a.png" alt="Step 10a: ControlBoard holder mounted" width="49%"> <img src="media/build/step09c.png" alt="Step 10: ControlBoard holder exploded view" width="49%">

<img src="media/build/step09b.png" alt="Step 10b: ControlBoards fitted" width="70%">

| Parts | Qty |
| :--- | :---: |
| `PCB_Halter` (printed, inserts fitted) | 1 |
| M5×12 + T-nut | 4 |
| ControlBoard V8 | 2 |
| M3×6 cylinder head screw | 4 |

1. Screw the holder to the front of the 2040 below the power supply: four T-nuts in the front slots, four M5×12 screws. Do this **before** fitting the boards, because the boards cover these screws.
2. Screw one ControlBoard to each angled wing with two M3×6 screws. The **screw terminals face outwards**, towards the servo connectors.
3. Wire each board to its servo and the power supply as described in the [PCB Assembly Guide](PCB_Assembly.md). The 25° angle leaves room for the servo plugs.

---

## Step 11 — Belt Adapters

| Parts | Qty |
| :--- | :---: |
| `BeltToJkkAdapter_31mm` (printed) | 2 |
| M5 screws for the carriage | 8 |

1. Screw one belt adapter to each carriage with four M5 screws. Because the adapter is 31 mm deep, these screws are 11 mm longer than with the original 20 mm adapter.
2. Move each carriage by hand over the full stroke and check that nothing rubs.

The adapters are the orange blocks on the carriages in the [step 14 picture](#step-14--belt-attachment).

---

## Step 12 — Pre-assemble the Hood

<img src="media/build/step11a.png" alt="Step 12: hood segments, seen from inside" width="49%"> <img src="media/build/step11b.png" alt="Step 12: assembled hood" width="49%">

| Parts | Qty |
| :--- | :---: |
| `Haube_oben_links` / `_rechts` (printed) | 1 + 1 |
| `Haube_unten_links` / `_rechts` (printed) | 1 + 1 |
| M3 screw + nut | 10 |

Assemble the hood on the bench before it goes onto the frame:

1. Lay the four segments out as in the left picture (seen from the open back). The joining tabs on the inside overlap the neighbouring segment.
2. Join the two upper and the two lower segments at the centre tabs, then the upper half to the lower half, with M3 screws and nuts.
3. Check that the joints are flush on the outside before tightening.

---

## Step 13 — Mount the Hood

<img src="media/build/step12.png" alt="Step 13: hood mounted" width="70%">

| Parts | Qty |
| :--- | :---: |
| Assembled hood (step 12) | 1 |
| M3 cylinder head screw | 16 |

1. Slide the complete hood on from the front. The 2040 passes through the cut-outs at the top and bottom, and the belt slots line up with the carriages.
2. Screw the hood to the upper and lower back plates with M3 screws into the inserts.

---

## Step 14 — Belt Attachment

<img src="media/build/step10.png" alt="Step 14: belt spacer with collar and M8x40 bolt (housing hidden)" width="70%">

| Parts | Qty |
| :--- | :---: |
| `Distanzhuelse_M8_15mm` belt spacer (printed) | 2 |
| M8×40 cylinder head screw | 2 |
| Soft tie-down loop (20 cm) | 2 |

1. Route each shoulder belt over its roller and down to the front of the housing.
2. Put the soft loop of the belt over the spacer shaft, so it sits between the housing and the Ø 20 mm collar.
3. Push the M8×40 screw through the spacer, then through the belt slot into the belt adapter, and tighten it. The collar keeps the loop from sliding off; the picture shows the result with the hood hidden.

---

## ✅ Final Checks

* Move both carriages by hand over the full stroke. The bolts must run freely in the belt slots.
* Check that no cable touches a carriage or a coupler.
* Continue with the firmware: **[Firmware Flashing Guide](Firmware_Flashing.md)**, then the **[SimHub Motion Setup Guide](SimHub_Motion_Setup.md)** (variant *Dual belt tensioner*).
