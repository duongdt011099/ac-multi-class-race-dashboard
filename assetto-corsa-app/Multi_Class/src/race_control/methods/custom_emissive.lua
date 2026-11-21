-- CSP custom-emissive flag boards (COTA's "lg_marshal_light_b_light", Barcelona's "DIGIFLAGS"): the
-- track config lists the material in a [CustomEmissive] section, so only the LED area of the texture
-- glows, and that glow is multiplied by the (dark) LED texture unless emSkipDiffuseMap is set.
-- While lit: emSkipDiffuseMap = 1 and the opaque alpha trick. The board body keeps its own lighting;
-- zeroing it like a plain board turns the whole board black.
local Base = require("src/race_control/methods/base")

return Base.Make("custom_emissive",
  { emSkipDiffuseMap = 1.0, ksAlphaRef = -193.0 },
  function(filter, config) return config.custom[filter:upper()] == true end)
