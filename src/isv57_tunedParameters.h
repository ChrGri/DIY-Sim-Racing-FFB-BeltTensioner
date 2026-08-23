#pragma once
#include <stdint.h>

#define ISV57_NMB_OF_REGISTERS 305

// Tuned parameters for iSV57 integrated servo motor
// Provides fast position response, stiff control loop, and prevents overshoot
const int32_t isv57_tuned_parameters[ISV57_NMB_OF_REGISTERS] = {
    500,     // Pr0.00: Reserved parameters
    0,       // Pr0.01: Control mode (0 = Position mode)
    0,       // Pr0.02: Real-time auto-gain tuning mode
    9,       // Pr0.03: Selection of machine stiffness at real-time
    110,     // Pr0.04: Ratio of inertia
    0,       // Pr0.05: Command pulse input selection
    0,       // Pr0.06: Motor rotational direction setup
    3,       // Pr0.07: Reserved parameters
    3200,    // Pr0.08: Microstep
    1,       // Pr0.09: 1st numerator of electronic gear
    1,       // Pr0.10: Denominator of electronic gear
    4000,    // Pr0.11: Reserved parameters
    0,       // Pr0.12: Reserved parameters
    500,     // Pr0.13: 1st torque limit
    500,     // Pr0.14: Position deviation setup
    0,       // Pr0.15: Absolute encoder setup
    50,      // Pr0.16: External regenerative resistor setup
    50,      // Pr0.17: Regeneration discharge resistance power
    0,       // Pr0.18: Vibration suppression - N after Stop
    0,       // Pr0.19: Microseismic inhibition
    0,       // Pr0.20: Activated pulse edge
    -1137,   // Pr0.21: Reserved parameter
    -729,    // Pr0.22: Reserved parameter
    0,       // Pr0.23: Reserved parameter
    0,       // Pr0.24: Reserved parameter
    600,     // Pr1.00: 1st position loop gain
    400,     // Pr1.01: 1st velocity loop gain
    500,     // Pr1.02: 1st time constant of velocity loop integration
    27,      // Pr1.03: 1st filter of velocity detection
    100,     // Pr1.04: 1st torque filter
    175,     // Pr1.05: 2nd position loop gain
    110,     // Pr1.06: 2nd velocity loop gain
    10000,   // Pr1.07: 2nd time constant of velocity loop
    8,       // Pr1.08: 2nd filter of velocity detection
    200,     // Pr1.09: 2nd torque filter
    0,       // Pr1.10: Velocity feed forward gain
    0,       // Pr1.11: Velocity feed forward filter
    0,       // Pr1.12: Torque feed forward gain
    1000,    // Pr1.13: Torque feed forward filter
    1,       // Pr1.14: 2nd gain setup
    0,       // Pr1.15: Control switching mode
    50,      // Pr1.16: Position control switching delay time
    50,      // Pr1.17: Control switching level
    33,      // Pr1.18: Control switch hysteresis
    33,      // Pr1.19: Gain switching time
    100,     // Pr1.20: Reserved parameter
    100,     // Pr1.21: Reserved parameter
    0,       // Pr1.22: Reserved parameter
    100,     // Pr1.23: Speed regulator-kr
    0,       // Pr1.24: Speed regulator-km
    0,       // Pr1.25: Speed regulator-kd
    10,      // Pr1.26: Filter
    0,       // Pr1.27: Reserved parameter
    10000,   // Pr1.28: 1st position loop integral time
    0,       // Pr1.29: 1st position loop differential time
    10000,   // Pr1.30: 2nd position loop integral time
    0,       // Pr1.31: 2nd position loop differential time
    10,      // Pr1.32: Position loop differential filter
    0,       // Pr1.33: Speed given filter
    0,       // Pr1.34: Reserved parameter
    0,       // Pr1.35: Position command digital filter Settings
    0,       // Pr1.36: Encoder feedback pulse digital filter Setting
    1052,    // Pr1.37: Special function register
    0,       // Pr1.38: Reserved parameter
    0,       // Pr1.39: Reserved parameter
    0,       // Pr2.00: Adaptive filter mode setup
    50,      // Pr2.01: 1st notch frequency
    20,      // Pr2.02: 1st notch width
    99,      // Pr2.03: 1st notch depth
    90,      // Pr2.04: 2nd notch frequency
    20,      // Pr2.05: 2nd notch width
    99,      // Pr2.06: 2nd notch depth
    2000,    // Pr2.07: 3rd notch frequency
    0,       // Pr2.08: 3rd notch width
    0,       // Pr2.09: 3rd notch depth
    2000,    // Pr2.10: 4th notch frequency
    0,       // Pr2.11: 4th notch width
    0,       // Pr2.12: 4th notch depth
    0,       // Pr2.13: Selection of damping filter switching
    0,       // Pr2.14: 1st damping frequency
    1,       // Pr2.15: 1st damping filter
    0,       // Pr2.16: 2nd damping frequency
    1,       // Pr2.17: 2nd damping filter
    0,       // Pr2.18: 3rd damping frequency
    0,       // Pr2.19: 3rd damping filter
    0,       // Pr2.20: 4th damping frequency
    0,       // Pr2.21: 4th damping filter
    15,      // Pr2.22: Positional command smoothing filter (PT1)
    10,      // Pr2.23: Positional command FIR filter
    0,       // Pr2.24: Reserved parameter
    0,       // Pr2.25: Reserved parameter
    0,       // Pr2.26: Reserved parameter
    0        // Pr2.27: Reserved parameter
};
