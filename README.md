# Monk Telemetry Logger

A high-precision data gathering plugin for FFXIV built on the Dalamud Framework.

Designed to specifically track spatial relationships and combat actions in Monk mirror match duels.

## Data Schema Output

Logs frame-by-frame data directly to 'Documents/ffxiv_duel_telemetry.csv' upon entering battle in the dueling ring. (Yes, it can be with a dummy if you smack it and run into the ring)

* `Timestamp`: Epoch time in ms
* `pX, pZ, pRot`: Player positioning coordinates, and rotation orientation vectors
* `tX, tZ, tRot`: Target positioning coordinates, and rotation orientation vectors
* `Distace`: The Euclidean distance between player and target
* `Facing Delta`: Directional angle difference in degrees
* `tCurrentHP, tMaxHP`: The current and max HP of the target
* `tLastFiredActionId`: Instant-cast weaponskill ID verification (animation-clip immune)

## Usage Instructions

How do I know? I haven't tested it