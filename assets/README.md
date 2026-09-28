# Canonical runtime assets

These assets were supplied by Akino for FLAMORIS MCP status/activity integration.
No recoloring, redrawing, recompression, cropping or dimension change was performed.
SHA-256 provenance in runtime/provenance.json proves byte identity to ZIP entries.
PSD, contact sheets, look-direction previews and pet metadata are not runtime inputs.

Use mcp-status-green_{16,20,24,32}.png when Status.Current.IsGreen, otherwise red.
Choose the closest supplied pixel size for DPI; preserve transparency/aspect ratio.
Green now requires an actual authenticated bridge or registered Hub connection; it
does not identify a particular AI. Always provide accessible status text.

For meaningful foreground activity, host code can decode chipsy-activity.webp
(1536×2288 RGBA, 8 columns × 11 rows, each 192×208). Use row 7 (running),
columns 0–5 as an animation, or column 0 as a static activity image. Select the
source rectangle in the host renderer; the canonical bitmap stays unchanged.
Restore the host's CURRENT tool cursor when activity ends, not a stale cached
cursor. Hosts choose display scale/hotspot and supply accessible activity text.
Core neither decodes images nor installs OS cursors.

The package ships these files under assets/runtime. Hosts explicitly opt into
copying the needed status sizes and the activity sheet; nothing changes app chrome
automatically. This is not a ChatGPT Pet installation.

## Asset rights

Supplied FLAMORIS artwork for this integration. Permission to include/reuse these
runtime images in FLAMORIS hosts comes from the task's explicit asset instructions.
Non-code artwork is not automatically Apache-2.0; no broader redistribution license
is asserted here. The code's Apache-2.0 license is unchanged.

## Shared WPF acknowledgement

The original runtime assets above remain byte-identical. The WPF package embeds a
losslessly decoded PNG rendering frame at `src/Flamoris.Mcp.Wpf/Assets/chipsy-acknowledgement.png`,
from row 7, column 0 (192 by 208), for Windows without an optional WebP codec.
It displays at 48 DIP (roughly 200% of Cutwork's tool glyph), centered for 500 ms
after successful connection or state-changing AI operations. This supersedes the
old cursor policy above for desktop consumers; the source sprite sheet is unchanged.
