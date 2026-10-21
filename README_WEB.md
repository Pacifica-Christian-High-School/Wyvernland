# Wyvernland in a browser

The Unity game can be exported as a desktop browser game, including the island,
physics, movement, menus, and test ground. It uses keyboard and mouse controls.
Touch controls are not implemented. Website players in the same map now see
each other's named avatars and movement through the hosted presence service.
Players create rooms for Island or Test Ground. Each room shares world objects,
held items, terrain edits, and destruction with its members.
The complete island uses substantial memory. The browser heap starts at 256 MB
and grows as needed up to Unity's supported 4 GB limit.
Browser prop batching has a 64 MB geometry budget and prioritizes nearby groups.
Other props keep their original renderers. Local loading range suspends distant
renderers, colliders and active prop scripts while preserving their shared state.

## Build

In PowerShell, from this project:

```powershell
.\Tools\Build-Web.ps1
```

The helper uses Unity 6000.0.32f1 with WebGL Build Support. It copies the current
Assets, Packages, and ProjectSettings into a separate project under Artifacts,
reuses installed packages, and builds without changing the open project's target
or player settings. Logs are saved in that separate project. Override the editor
or destination with `-UnityPath` and `-OutputPath`.
Subsequent exports reuse the isolated build cache. Use `-Fresh` to create a new
build snapshot. Python 3.9 or newer is required for the hosting preparation step.

The export goes to `Sites/Wyvernland/public` for the hosted game. The custom webpage source lives in
`Assets/WebGLTemplates/Wyvernland`. Keep the entire export together when hosting,
including Build and any StreamingAssets. Serve over HTTP or HTTPS; double-clicking
index.html does not work. Gzip with Unity's decompression fallback lets a regular
static host serve the compressed files without special Content-Encoding rules.
Large world files are split into verified chunks by `Tools/prepare_web.py` and
joined by the webpage before Unity starts. Run that preparation tool if exporting
directly through Unity's menu rather than the PowerShell helper.

## Local preview

```powershell
python .\Tools\serve_web.py
```

Open http://127.0.0.1:8765 to inspect the static export. To play with rooms,
use the Sites checkout's development server (`npm run dev`) after installing
its dependencies, generating migrations, building once and applying the
generated `drizzle/*.sql` migration to the local `DB` binding with Wrangler.
`node Tools/test_shared_world.mjs http://127.0.0.1:5173` checks room creation,
held objects, destruction, late joins, host transfer, and saved world changes.
`node Tools/test_online.mjs http://127.0.0.1:5173 --expiry` checks presence and expiry.
Create a world and choose its map before clicking Play Wyvernland. Friends join
with the room code or invite link. Play creates a new world if none is selected.
Missing rooms and invalid invite links redirect to the main site without the
room parameter. If an active room disappears, the client stops its session and
returns there too; temporary connection failures continue retrying.
Terrain uses 100m × 100m tiles; pit processing rebuilds only touched tiles and
releases temporary preparation geometry between tiles. Cutter simplification
updates nearby mesh edges through a priority queue instead of repeatedly sorting
the whole stone. Cutter history stays in compact vertex/index arrays; polygon
faces are expanded for the active tile and released after it is cut.
Pit Boolean operations use coordinates near each cut region's origin so distant
parts of the island do not accumulate intersection rounding errors.
Loading also yields between cutters in dense piles, allowing the browser to
collect temporary geometry before processing the next part of the pile.
For the full browser island, the exporter calculates deterministic pits once
and saves their finished tiles and compact cutter history in the browser data.
Choosing Enter Wyvernland loads those tiles instead of repeating thousands of
Boolean operations in WebAssembly. The export cache is invalidated when terrain,
rock placement, or the pit algorithms change. Test Ground still cuts its small
sample population during loading.
Pit operands use roughly one quarter of the stone faces (12–64 triangles),
retain the stone's footprint and depth, and discard unused vertices before
saving cutter history. The webpage advances Unity's loading frames with a
worker clock when the tab is hidden, so downloading and map preparation
continue while switching tabs or minimizing the browser. The loading clock
stops when preparation finishes unless that browser is the room's physics host.
The host keeps the room moving while its tab is hidden; browsers may still
throttle or suspend background pages, in which case another active player can
take over after the session expires.
The webpage's Fullscreen button
expands the game. Click inside the game to capture the mouse; Escape releases it
and pauses. Back to Website returns to the launch page.

Fullscreen requests keyboard capture for Escape where supported. A quick Escape
press still reaches the game menu; holding it continuously for three seconds
requests fullscreen exit and shows a hold-progress hint. Release, focus loss, or
leaving the page cancels the timer. Browser emergency exit shortcuts take
precedence: Chrome can exit after its own two-second hold, and browsers without
keyboard capture may exit on a short press. The Exit fullscreen button remains
available. Keyboard permission denial never blocks entering or leaving fullscreen.

Player progress and options are stored locally for that browser and site.
Switching tabs saves local progress and pauses player controls. Room object
positions, ownership, tree supports, rope links, actual destruction meshes, and
removed objects are saved in D1 and restored for later visitors. When everyone
leaves, the saved world remains available and physics resumes on the next join.
Clearing browser data removes local settings but does not remove room changes.

## Browser performance

The browser starts with low texture quality and disables better light and depth
of field once for existing browser settings. Players can raise quality in Options.
The browser camera draws to 1500m and grass draws within 128m, visiting at most
121 nearby patch candidates instead of scanning the entire island every frame.
Dense prop cells split into bounded draw-cache batches under the existing 64 MB
geometry budget. Options > Graphic > Loading Range sets a local 50–2000 m radius
(default 200 m). Each browser uses its own range and player position. Distant
stationary props and terrain tiles stop rendering and colliding; batched sources
are hidden individually. Moving or held objects always stay active, including
objects held by other players, with a 20 m spherical collision neighborhood.
Fast-moving objects also prepare collision geometry along the next physics step.
Loading preserves rope anchors, intentionally disabled colliders, destruction
meshes and network identities. It never publishes a world deletion. Sleeping
bodies resume without being woken solely by a range change. Shared world data
and meshes stay resident for reliable restoration: this reduces active simulation
and rendering, not the initial download size or all mesh memory.

Shared-world startup snapshots dynamic objects only. Idle polling probes motion
before allocating snapshots, tracks owned objects directly, and refreshes collider
settings only after geometry or body hierarchy changes. Rope links retain their
sleeping state when room physics starts or transfers; idle follower ropes do not
rebuild their meshes each frame. The export runs a native regression fixture with
3000 static props, 16 ropes, idle polling, follower visuals, and moving host transfer.
This checks work counts and correctness, not browser frame rate.

World download chunks use individual content hashes and persistent browser cache
storage, with two concurrent requests. Unchanged chunks can be reused between
visits and game updates. Cache failures fall back to downloading normally. The
first island download and full resident world still require substantial memory.

## Hosting

Stones and mineral fragments follow the holding hand without pose interpolation,
and heavy stones use a responsive arm return. Either hand can charge and throw.
With separate objects in both hands, hold Aim while starting the Drop charge to
select the left hand; otherwise the right hand takes priority.
Held-object direction alignment runs at 2.16 instead of 1.2 (80% faster).
Hits close and reopen curved eyelids over the local view within 0.3 seconds;
player meshes keep their normal colors and Medium-quality dark shadow settings.
Detached fragments refresh their Rigidbody centers from their own mesh geometry,
including scaled pieces, later cuts, room mesh updates, branches, and trunk halves.
Fast impact sweeps, damage overlaps, and room-confirmed damage respect collider
exclusions, so releasing a held object cannot bypass its thrower's ignored body
collisions. The same projectile can still hit other players and the training dummy.

Settled injured limbs and corpses suspend ragdoll simulation when no moving object
is within 10 metres. Nearby motion, a new impact, or pickup wakes physics while
preserving injuries and pose. Falling and carried bodies remain simulated.
For similar-hardness collisions, destruction remains at 30% unless both objects
have hardness and resilience of at least 7. Above that threshold, all four stats
contribute equally and linearly, reaching 1% when all four are 10.

The Sites checkout has its own `.openai/hosting.json` with its registered
project ID and a D1 binding named `DB`. Its Worker serves the game and the
`/api/players`, `/api/rooms`, and `/api/world` endpoints. Reuse that ID for
subsequent publication.
The frontend sends positions every 250 ms after the selected map is ready;
remote avatars interpolate received positions. Sessions use server-issued
tokens, expire after 20 seconds without a heartbeat, and leave immediately
on a normal page exit. Background tabs send slower paused heartbeats.
Public visitors can play anonymously and choose a name before entering.
One ready player simulates free objects; the server elects a replacement when
that player leaves or expires. The server enforces exclusive pickup ownership,
accepts world commits only from the current host, and keeps each room isolated.
Guests send interactions to the host, which publishes the resulting geometry
instead of asking every browser to repeat randomized destruction. Held objects
use their owner's latest pose while retaining the host's destruction metadata.
Mesh data is compressed, hashed, and uploaded in chunks. Unchanged objects are
generated locally with stable IDs, reducing saved state and join traffic.
Room codes are shared access keys: anyone with a code can join its world.
Rooms retain a baseline fingerprint and reject incompatible generated worlds.
The Unity project ignores Sites exports and Artifacts snapshots;
the templates and build tools remain normal project source.
