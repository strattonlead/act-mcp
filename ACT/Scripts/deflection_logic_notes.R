# Deflection Calculation Notes (verified against Heise 2013 Interact Guide)
#
# Deflection = sum of squared differences between fundamentals and outcome transients.
# Deflection = (Ae'-Ae)^2 + (Ap'-Ap)^2 + (Aa'-Aa)^2
#            + (Be'-Be)^2 + (Bp'-Bp)^2 + (Ba'-Ba)^2
#            + (Oe'-Oe)^2 + (Op'-Op)^2 + (Oa'-Oa)^2
#
# This is the SUM of squared differences, NOT the square root (Euclidean distance).
# The Interact program reports this raw sum as "Deflection".
# Actor tension = (Ae'-Ae)^2 + (Ap'-Ap)^2 + (Aa'-Aa)^2
# Object tension = (Oe'-Oe)^2 + (Op'-Op)^2 + (Oa'-Oa)^2
# Total Deflection = Actor tension + Behavior tension + Object tension
#
# Transient Chaining:
# - Event 1 in a situation: input transients = fundamentals
# - Event 2+ in a situation: input transients = outcome transients from previous event
# - New situation: reset to fundamentals
# - Behavior always uses its fundamental EPA as input (resets per event)
# - Deflection is ALWAYS computed against fundamentals (not previous transients)
