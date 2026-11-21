-- Plain flag boards (Imola 2024's "digiflag"): the track config tints the whole material's glow.
-- While lit, only the glow shows: no diffuse / ambient lighting underneath (that washes the colour
-- out) and the alpha test always passes, so the board is fully opaque (ksAlphaRef = -193, the trick
-- the CSP configs of Nordschleife / COTA use). The fallback for any board no other method claims.
local Base = require("src/race_control/methods/base")

return Base.Make("plain",
  { ksDiffuse = 0.0, ksAmbient = 0.0, ksAlphaRef = -193.0 },
  function() return true end)
