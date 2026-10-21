# Wyvernland terrain prototype

Open `Assets/Scenes/SampleScene.unity` and press **Play** to open the main menu. Choose **Enter Wyvernland** or **Test Ground** to generate that map. Startup does not generate either map.

Terrain uses 100 × 100 metre tiles. Pit cuts are queued by location and rebuild
only affected tiles, releasing temporary face caches between tiles. Loading
shows both pit preparation and tile-cutting progress.

World creation displays loading progress and prepares trees, vines, rocks, grass,
terrain Boolean pits, physics registration and rendering in batches. Simulation stays paused until
the complete world is ready. The main map can still take time to generate;
the loading window continues updating throughout population creation.
Loading continues when the game window is unfocused or minimized.

## Controls

- **WASD** — move
- **Left/Right Shift + W** — sprint
- **Space** — jump
- **C or Left Control** — crouch; press while sprinting to begin a slide
- **Mouse** — look
- **Backtick (`)** — toggle Console (off by default). Gameplay labels, control
  instructions, object and destruction information, and tree debug balls and
  labels appear only while Console is on. Rebind Console in Options > Keybinding.
- **Hold Interact + Left Mouse** — reach with the right hand. **Hold Interact +
  Aim (Right Mouse by default)** — reach with the left hand. If the chosen hand
  is occupied, the other free hand reaches instead. Pickup happens only when
  the hand's trigger touches the object. Objects up to 26 kg use one hand;
  objects over 26 kg and up to 90 kg require both empty hands and contact from
  both triggers. Objects above 90 kg use every free hand to drag instead. Each
  contacting hand contributes up to 0.3 m/s toward the player, falling linearly
  with the extra mass above 90 kg to zero at 200 kg. Dragged objects keep gravity
  and solid collision active. Releasing either
  input ends the reach. Return speed decreases evenly above 2 kg, reaching
  50% of normal at 26 kg. Aim at any visible mesh face to start pickup. The
  hand reaches the nearest vertex on that face, or the aimed face point when
  a long low-detail face has no nearby vertex.
  Held sticks are centred through the grip at the thumb's local X position;
  reaching still uses the aimed surface point.
  Valid hand contact attaches the object in the same rendered frame, including
  when contact already exists as reaching starts. Contact uses a 1 cm tolerance
  and is checked without waiting for trigger callbacks or nearby-list refreshes.
  Room pickups predict the local grip while ownership is checked; only confirmed
  grips publish shared physics, and rejected claims restore the shared pose and
  free the hand. Tree support changes wait for ownership confirmation.
  Room reservations keep unheld props under host gravity. Cancelling a reach
  releases only the reservation and preserves static tree support. Guest-only
  kinematic settings are not saved as world physics; loose frozen props recover
  gravity when restored or transferred to a new host. Late held snapshots cannot
  overwrite an already released object's pose or gravity.
- **H (Switch)** — exchange the items held in the left and right hands.
- **Attack (Left Mouse) while holding an item** — release before 0.7 seconds for
  the Punch animation. Holding past 0.7 seconds keeps the strike active until
  release. The attacking arm raises into a bent-elbow guard. Mouse movement
  steers a continuous shoulder-led arc on both axes, with immediate response,
  a short damped follow-through, and a steady held pose when the mouse stops.
  The hand retains its steered direction and reach until further input or release.
  Held objects keep their captured local grip direction throughout the held
  strike, rebound, and return, while following the arm's rotation.
  Slow contact stops both translation and rotation at solid surfaces, including
  thin obstacles crossed between frames. The hand stays on the grip, and pulling
  or rotating away releases contact immediately.
  On contact, similar-hardness solids at destruction speed share one destruction
  roll, decreasing from 30% to 1% as both materials approach maximum strength.
  A failed roll bounces the striking arm. Other hits apply
  the material destruction rules first and rebound from a harder solid while
  the relative contact speed exceeds 3 m/s. Every bounce reverses that relative
  speed initially, then slows linearly to rest over a fixed 0.12 seconds before
  the arm returns. Once that speed
  is exceeded, sustained fast mouse movement builds up to 50% extra waving
  speed over 1.5 seconds. The bonus fades when movement slows and resets for
  each strike. The top-left HUD shows object speed, waving bonus and threshold.
  The arm extends through a stroke while keeping a bent elbow and nearly
  natural limb proportions; camera movement from the swing is subtle.
  Held-strike camera rotation is 40% slower across both slow and fast mouse
  movement, while weapon steering and the configured sensitivity stay the same.
  The right hand attacks when occupied; if only the left hand holds an item, it uses the left hand.
- **Aim (Right Mouse by default) while holding an item in the left hand** — the
  same held strike with the left hand, including its mirrored guard, mouse steering,
  short-click Punch, and smooth release. Custom Aim bindings also work. The button
  that starts the strike controls its release; pressing Interact still switches to
  pickup/reach instead.
- **Escape** — pause/resume. In submenus, Escape goes back (or cancels a key rebind).

Dead bodies and severed limbs can be destroyed by sufficiently strong collisions,
including held-weapon hits. Each remaining arm and leg separates from the torso
into an independent solid fragment in its current pose, without skeleton or
ragdoll joints. Already severed limbs break into a few large chunks. Held loot is released, and the
fragments and original body's removal persist in shared rooms.

Small destruction fragments stay visible permanently with their original mesh,
scale, and materials. Once a loose piece has stopped translating and rotating
(or physics puts it to sleep), its Rigidbody and colliders are removed. Moving,
held, and temporarily frozen pieces keep their physics. Shared rooms preserve
the settled appearance without recreating physics, including for late joiners.
The fragment cleanup key also leaves these small pieces in place.

The pause menu contains Resume, Options, and Quit Game. Options contains Graphic, Keybinding, Audio, and Back. Graphic controls Better Light Effect (enhanced sunlight, shadows, ambient shading and post-processing), grass density (0–100%, default 100%), fullscreen, VSync, field of view, and supported display resolutions with an Apply Resolution button. Resolution changes work in standalone builds; Unity controls the Game view resolution in the Editor. Keybinding supports movement, sprint, jump and mouse sensitivity; assigning an occupied key swaps the two actions. Audio controls master volume and mute. Settings save when leaving an options page, and applied resolutions are remembered. Quit Game exits a build or stops Play mode in the Editor.

The procedural player animation freezes both legs in their exact takeoff pose for
the first second airborne. Airborne yaw remains unrestricted but smoothly slows
to 10% input sensitivity with a 30-degree-per-second cap, then smoothly restores
normal turning after landing. A longer fall then blends into a walking-speed leg
swing while the arms remain governed by grounded movement; landing restores the
normal grounded cycle.
At rest, the arms spread 20 degrees from the body and the legs spread 10 degrees.
Both hips and both knees also hold a 10-degree bend while grounded and idle.
The visible player skin has a compound set of solid bone colliders fitted from
its vertices at initialization. Torso/head boxes and limb/finger capsules follow
their deform bones, including wrist rotation and stretched arms. Local shapes
share the movement Rigidbody and preserve the existing foot support; held
objects ignore them. Remote avatars use the same rig fitting on a kinematic
body, so their walk and crouch poses also move their colliders. No animated
triangle mesh is baked or cooked each frame.
Leg spread closes to zero while moving. Arm spread increases smoothly to 30 degrees
while moving, and each free arm swings opposite its same-side leg. A held object's mass
reduces that arm's walking and sprinting swing amplitude continuously, starting
at 80% of normal for a light item and reaching 10% at the 90 kg pickup limit;
a two-hand hold reduces both arms. Hands holding a stick have no walking or
sprinting arm swing, including both hands of a two-hand grip. Held arm swing speed decreases linearly to
99% of normal at 60 kg and remains there above that mass; free arms follow the
leg cadence. Sprinting directly reuses the walking pose with 35%
more swing amplitude and a 25% faster cycle; the old separate sprint pose is
removed. Ground probes originate 0.15 m above the player's
base for accurate airborne transitions.
The idle stance includes a subtle opposing stride: the left foot and right hand sit
slightly forward while the right foot and left hand sit slightly backward. This
offset fades out as soon as movement begins.
Crouching moves at 50% of normal walking speed, adds 45 degrees of bend to every
hip, knee and ankle, and spreads both arms by 40 degrees. A slide begins only from
an intentional Crouch press while sprinting on a valid slope.
Three ground probes (left, right and centre) maintain contact over faceted terrain.
Sprint animation also has a 0.22-second contact grace window for tiny high-speed
surface gaps; upward jump velocity bypasses the grace window immediately.
When the player leaves the ground while walking or sprinting, the arms continue their
movement cycle at the takeoff speed and sprint amplitude for 0.8 seconds, then blend
into the wide airborne pose. A stationary takeoff uses the wide pose immediately.
In that pose both arms spread widely to 70 degrees until ground contact is restored,
and both shoulders lift the arms 55 degrees forward. Left and
right foot support are evaluated independently with downward casts plus close-contact
spheres; contact from either foot prevents the player being classified as airborne.
During a slide, separate world-space smoke emitters follow the terrain contact beneath
the bottom-centre of each animated foot collider, so new smoke originates under the
soles while older particles trail behind. The gameplay HUD has no centre crosshair.
Holding Jump during a slide applies continuous slowdown only while the player is
touching the ground, keeping the braking arm-contact animation and hand smoke active.
There is no timed slide exit; sliding stops only when planar velocity falls below 1 m/s.

## Scale and terrain

- One Unity unit equals one metre.
- The player capsule is exactly 1.8 m tall.
- The terrain is 5,000 m wide (X) and 6,500 m long (Z).
- Coastlines and interior contours are manually traced from the photograph (30 loops, 958 points), including the northern bay and narrow southeastern inlet. This is a close interpretation of pencil lines, not surveyed or pixel-exact geometry. A top-down trace is available in `Artifacts/traced-map.svg`.
- The contour interval is 100 m: the four mapped lines represent 0 m, 100 m, 200 m, and 300 m.
- Subtle Perlin noise fades to zero at the contour boundaries to preserve their elevations. The mesh uses 10 m cells clipped to the traced outlines, with independent normals per triangle for the low-poly style.
- Elevation transitions use zero-slope blends at every contour. Terminal contours
  rise into bounded rounded summits, and elevated detail vertices are slope-limited
  against their cell boundaries to prevent isolated spikes and sharp mountain spines.
- Three flying islands have flat-shaded mesh surfaces, exposed cliffs and tapered undersides. There is no ocean or rectangular seabed. The 0 m contour is the island rim, not sea level.
- The four red-marked regions from 3qr3d.png are rolling plains. Their terrain blends gradually into nearby hills and varies through broad swells, smaller undulations and subtle detail instead of becoming perfectly flat.
- A diffuse-only clay shader mixes broad earth tones, warped clay patches, clumps, sparse mineral streaks and fine pores in world space, without a repeating texture tile. Small details fade with distance to reduce shimmer. The sky and distant haze use a bright pale cyan.
- The terrain color regions traced from `dfv3.png` blend grey, red-brown and
  yellow-brown subtly into the original clay, which remains the dominant color.
  The broad blend zones occupy most of each marked region, with only a small
  central core reaching the maximum tint. Soft boundaries fade into the base color,
  while overlaps mix their contributing colors. These regions affect the island
  tops; cliffs, undersides and procedurally generated trees retain their own clay colors.
- Falling below the islands returns the player to the starting point.
- Lighting uses warm 5600 K sunlight, cool sky fill and darker underside ambient light. The clay receives soft sun shadows and screen-space ambient occlusion while staying diffuse and matte. PC shadows extend 160 m with a 2048-pixel atlas; neutral tone mapping controls bright highlights and SMAA smooths edges.
- The sky uses a deep-blue upper hemisphere, a broad white-to-blue horizon transition, and a pure-white lower hemisphere beneath the floating islands. A warm sun disk, soft solar halo and near-white distance fog complete the atmosphere.
- The underground area is deliberately not implemented yet.

The main tuning points are in `Assets/Scripts/ProceduralMap.cs`; physical scale and palette constants are in `Assets/Scripts/WorldConfig.cs`. Terrain meshes and ground feature seeds are saved under `Assets/Resources/Terrain` so the game loads them at startup. After changing terrain generation or ground feature layout, use `Wyvernland > Rebuild Terrain Cache` in the Unity Editor.

Trees are generated from repeatable random seeds, with flat-shaded tapered trunks,
branches and forks. Tree heights are uniformly randomized from 3–20 m;
trunk thickness and branch spread scale with height and vary independently.
Every tree and stump has 4–7 low-poly buttress roots that radiate from the trunk,
bend independently and taper outward. Every terminal root cross-section is buried
below the terrain so no root ends appear cut off above ground. Root length, width,
height and bend spread were initially reduced to 60% of their original proportions;
root width is subsequently increased by 80% to 108% of its original proportion.
Every full tree randomly chooses one of the five silhouette families shown in
`codex-clipboard-30953ec6-86e6-4c68-b9fa-077547849057.png`: high fork with a low
limb, alternating climbing limbs, curved trunk with upright arms, tall short-limbed
trunk, or narrow crown fork. Branch height, rotation, length, rise and optional
forks vary within each family, as do trunk lean and curvature.
Near the player, every fork, side branch, trunk segment and root has its own
closed mesh, collider and Wood destruction component. Collisions use each
section's bounds for the Fracture size rule, rather than the height of the
entire tree. Root meshes have caps beneath the trunk so impacts can produce
valid fragments. Each branch has Hardness 3 and Resilience 4; trunk segments
and roots keep the Wood catalog's Hardness 2.5 and Resilience 7. Every generated
part remains resident with its collider enabled at every distance.
Branches attached to a fallen tree section detach individually when their own
colliders touch terrain, keeping the section's motion as they fall. Branches
that have not touched terrain remain attached to their supporting section.
When aiming at a tree part, the HUD shows its trunk destruction progress and
the reason the last destruction did or did not split it. Nearby trunk hits
count toward the threshold; branch or root destruction does not. The threshold
scales linearly from 3 to 15 destructions between the smallest and largest
generated average trunk radii. Detached trunks use the center of their remaining
connected trunk meshes as the Rigidbody center of mass; branches, resin and
the split-point sphere do not shift that center. Each resulting half starts with
zero destruction points and an independent tracker, so both grounded and falling
pieces can split repeatedly after collecting another full set of nearby points.
Trunk impacts accumulate in neighborhoods of five times the tree's average
trunk radius. When enough impacts cluster, the trunk splits at the last
destruction point, including its hit height, rather than at the tree midpoint.
Cut planes stay crosswise in each trunk piece's local frame; division-angle
retries rotate around local X. Detached fragments retain this frame, so later
cuts follow a fallen or rotated trunk rather than a world-horizontal plane.
The split check uses the current solid geometry, including child remainders
created by chipping, so an emptied original renderer cannot hide live trunk wood.
Chipping tests cavity floors against the remaining solid. Trunk cuts preserve
small cap edges and existing cavities as damage accumulates.
About one in seven placements is a capped stump, 0.8–1.92 m tall.
Normal full trees can grow zero to three Black Resin lumps on their trunks.
Trees below 10 m can grow at most one, trees from 10 to 24 m at most two,
and larger trees at most three. Each tree's growths are random but stable
for each generated tree. Resin is a soft, pickable solid; stumps and burnt trees
have none. Destruction on the tree within 1 m of a resin growth makes it fall from the tree
as a pickable object.
Their colors range from dark red-brown to a brightest brown reduced by 60%, with
every final tree color blended 50/50 with the terrain's base ground color.
Trees and stumps occur only inside the blue regions painted on `3qr3d.png`.
Their placement probability fades smoothly from the full forest density at the
centre of each painted band to zero at its boundary.
The lime regions painted on `dfsfs.png` add dense woodland, using a 27 m
jittered placement grid with a smooth edge fade. Woodland trees are 10–40 m tall
and do not become stumps. All tree trunks now vary more strongly in thickness,
base flare, taper, polygon count, lean, bends and segment count.
Outside dense woodland, the jittered tree spacing is 45 m and the darkest bark
color is 30% brighter. Every tree receives a persistent 0.5–1 m grass cluster
within roughly 5 m, including when it stands outside the main grass mask.
Each loaded tree has a matching static MeshCollider. Placement avoids cliffs,
the island void, high summits and the initial player spawn.

The world is generated once. Graphic > Loading Range sets a per-player 50–2000 m
radius (default 200 m). Outside it, stationary objects suspend rendering, collision
and active prop scripts. World data and meshes are retained for restoration and
shared destruction. Moving and held objects always remain active, with a 20 m
spherical collision neighborhood and preparation along fast throws' next physics
step. Each client uses its own position and range. Terrain uses 100 m tiles, and
stationary draw caches hide each source independently. Reloading preserves damage,
rope supports and intentionally disabled colliders without changing shared IDs.
The shader reconstructs flat face normals; collision and excavation keep the
original full surface.
The subtly warm, pale brown-blue fog starts
at 135 metres and reaches full strength at 1,650 metres. Terrain and grass use an explicit runtime fog state so
standalone builds match the editor; the Fog toggle still controls it.

Rocks and stones use stable random seeds across the entire landmass. A denser 62 m
placement lattice now favors 2–2.8 m rocks while retaining occasional
2–8.5 m forms, and colors vary from the ground clay toward brown-grey. Every rock
uses a 38% lower, broader vertical profile. A separate landmark pass adds rare
10–30 m rocks mainly to flat terrain below 72 m altitude. All generated rock types
use broad irregular upper caps narrowed by 20% instead of pointed cone profiles.
Embedded rocks use
the same family of broad-topped forms, are scaled 70% larger again, and keep about
half their mass below the surface. Individual dark-grey stones are 0.04–0.5 m and
sit slightly below ground. Roughly 68% of large rocks receive a
nearby irregular 3–15 m stone pile composed of terrain-conforming 0.2–0.7 m stones.
Above 80 m, pile footprints and their constituent stones grow smoothly with altitude,
reaching up to 1.65x the footprint and 1.7x the stone size near the highest summits.
Some rocks instead sit at the center of a 5–10 m surrounding pile; stones in these
piles grow smoothly toward a 1.1 m maximum near the central rock. A much smaller
background chance also creates standalone piles away from rocks.
Above 250 m, a separate rock-pile type becomes common: 82% of generated rocks anchor
a dense 7–20 m-radius cluster of full-sized 2–6.5 m rocks. These rock piles are
independent of the existing small-stone piles and use combined static MeshColliders.
Within rock-adjacent piles, per-stone color smoothly changes from the parent rock's
color nearby to the normal grey stone palette farther away. The stone palette is
independently randomized per stone from dark grey to normal grey. After parent-rock
influence, every pile-stone color receives a final fixed 30% terrain-color blend.
Rocks and stones are more common in the red regions painted on `asfaff.png`.
After all stones and rocks are placed, loading precomputes their Boolean pits at
their original buried positions, including every stone-pile and rock-pile piece.
Preparation yields between batches and rebuilds each affected island once before
grass, world rendering and gameplay are ready. Closed cutting meshes use half
the original stone or rock faces. Pits have matching
MeshColliders, and use 45% darker terrain shading on their interior facets. Terrain
remains whole; preparation updates only affected islands' meshes and colliders.
Pickup and rock destruction reuse the preloaded pits and never rebuild terrain.
These cutouts last for the
current generated world session.

Stone meshes use 8–12 sides and 5–7 vertical bands for denser, more complicated
faceting while preserving their angular style. Rock profiles use 9–13 sides and
seven irregular height rings. Stones are sunk against the final sampled
terrain surface. Stones larger than 0.4 m receive low-profile capsule colliders;
smaller stones remain permanently non-colliding. Large rocks retain static
MeshColliders over the full range. No generated ground props use Rigidbodies.
Rock and stone render materials are double-sided, standing rocks have corrected
top/bottom cap winding, and an extra narrowing underside ring rounds each base below
the terrain instead of ending in a flat open-looking plane.
Embedded rocks add shallow deterministic depressions to both the terrain mesh and
terrain collider using existing shared terrain vertices. Feature-local topology
cuts are deliberately avoided because unmatched cuts across contour triangles can
open long terrain seams, especially near rock-adjacent stone piles. Small stones
remain slightly sunk without subdividing the island mesh. Grass is excluded beneath
rocks, stones and piles.
Rocks, stones and piles stay resident at every distance.

Stone piles contain sparse 0.18–0.45 m grass between their stones, including piles
outside the main grass region. Rock and individual-stone grass exclusion radii are
reduced by 80%, leaving only a tight clear patch at each object's centre.

Five ancient monuments use the supplied `Ancient Monument.blend` geometry, exported
to `Resources/Models/AncientMonument.fbx` for deterministic Unity import. Each copy
preserves the source's three-part faceted silhouette while varying in scale and
rotation. They are 200–500 m tall, use a
yellow-brown/grey stone blend, and spawn only on relatively flat terrain below
78 m altitude. Their well-separated sites and complete footprints are restricted to
the main island, so neither small island receives a monument. Monument meshes remain
resident at every distance, with their static MeshColliders always enabled.
Each monument registers its complete footprint plus a 3 m clear perimeter before
environmental props are placed. Trees and grass cannot spawn in that zone; rocks,
rock piles and stone piles also include their own radius in the clearance test so
none of their geometry enters the perimeter.
The monument material is double-sided as protection against visible face gaps.
Placement samples terrain across 15% of monument height in all four directions, and
the base is buried by 4% of total height so large monuments cannot float above plains.

Grass follows the green annotation in `dsfsdfs.png`, including its unpainted
central hole. Three pixel silhouettes reproduce the reference grass as fixed,
double-sided flat geometry, with no camera-facing billboarding. Base map density is
reduced by 30%; the separate tree and stone-pile grass densities remain unchanged.
Each clump has
a random height of 0.4–1 m and opacity of 40–60%. Broad seeded regions each use
one of the reference tan, burnt-orange or dark earthy-brown colors. Color variation within each
266 m region is restrained but 60% broader than the original variation, and
differently colored regions mix across smooth 198 m transitions. Terrain normals orient the
clumps, with random rotation around that normal. Seeded 7 m patches select one,
two or all three types. Stratified placement targets roughly 44–50 clumps per
4 m-radius circle in fully covered terrain. Density fades smoothly over 150 m
when approaching the outer green boundary or the central unpainted hole, and
steep cliffs can further reduce the realized count.
Grass keeps one shared mesh and persistent surface/distribution maps for the whole
world. Instanced GPU drawing constructs clumps from these maps; camera visibility
and subpixel size determine draw submission without creating or unloading objects.
Depth-writing dithered coverage reduces overlapping grass blending. It has no colliders.

## Rendering and update costs

Stationary props share cached draw meshes in 128 m groups, with their original
colors stored in vertices. Their gameplay meshes and colliders remain active and
unchanged. Pickup, falling, or destruction removes the affected prop from the draw
cache and restores its own renderer. Terrain remains one whole mesh per island.

Terrain colors and clay noise are cached once instead of recalculating their
procedural functions for every shaded pixel. The PC renderer uses Unity's GPU
Resident Drawer and Forward+ rendering. Shadows cover 160 m with one 2048-pixel
map; ambient occlusion uses half resolution after opaque drawing and reconstructs
normals from depth, avoiding a scene-wide normals pass. The unused opaque color copy
is disabled. Physics uses automatic box pruning and reuses collision callbacks.

Pickup lookup uses a spatial index, maintained only for moving objects. A single
update component poses held objects. Tree support is checked on geometry changes;
intact tree parts do not receive recurring physics callbacks. Destruction registration
and texture-quality application happen on creation or setting changes. Fragment spawn
queues, frame-rate gates and spawn limits have been removed.

Run `Tools > Wyvernland > Validate Resident World` to check whole-surface pits,
resident stone identity after observer movement, tree/resin destruction, and pickup
movement across spatial-index cells.

## Rope objects

Trees no longer generate hanging vines. The Rope object simulation remains
available for existing objects and physics validation. It uses linked rigidbodies
with capsule colliders, constrained joints, and a continuous visual tube.

## Physical destruction

The top-left HUD shows Hardness and Resilience for the item in hand and, below
it, the object or terrain under the camera's aim. Test Ground has a golden
0.3 × 0.3 × 0.3 m Stone at its center with Hardness 10 and Resilience 10. An
eight-sided iron bar, 5 cm in diameter and 1.4 m long, lies 2.5 m ahead of the
player at the Test Ground spawn.

Generated rigid physical objects receive a `PhysicalDestructible` component.
The player and its child colliders are excluded. Whole islands and rendered objects
are also registered. Generic Stone-material objects have a per-object Hardness in
the catalog's 4–7 range. Rock objects are biased toward lower values and Stone
objects toward higher values. The value is repeatable from the object's name and
position, so each object has consistent hardness. Fragments inherit it from their
source. Other materials use the midpoint of their catalog hardness range;
Iron's range is 6–8, giving the test bar Hardness 7.
Stone's material density is 2,295 kg/m³, 15% below its previous value.
Resilience uses the catalog value (Wood is 7). Both are editable per object. Clay, mud, soil
and resin carry the `Soft` label; the current clay
terrain is Soft as well.

The effect list contains Fracture, Shatter, Depression, Slicing and Abrasion.
Held strikes between solid objects whose Hardness differs by at most 2 use one
destruction roll for the whole contact, regardless of Resilience similarity.
The chance decreases from 30% to 1% as both objects' Hardness and Resilience
approach 10. Otherwise the holding hand rebounds at the relative collision
speed initially, slowing linearly to zero over a fixed 0.12 seconds. Material speed and geometry limits
still apply. Both objects share the roll, repeated contact callbacks cannot
reroll it, and multiplayer forwards that same decision to the physics host.
Terrain and Soft objects keep their existing responses. Ordinary unheld collisions
use the rules below.
When both Hardness and Resilience differ by at most 2 in either direction,
each object independently has a 40% chance to be affected on the initial
collision, provided its speed threshold is met. Similar solid objects select
Fracture; similar Soft objects select Depression (geometry deferred). Outside
this similarity range, the impactor must have strictly greater Hardness. Soft
targets select Depression. Other targets fracture when the impactor's Resilience
is lower or within one point above the target; greater differences select Shatter.
When both objects have Hardness and Resilience below 4, each eligible object's
destruction chance starts at 40% and gains ten percentage points per Hardness
point below 4, up to 70% at Hardness 1 or lower. The smaller object gets an
additional size-based bonus of up to ten percentage points. The normal effect,
speed and minimum-size gates still apply.
The similarity tolerance is editable. Destruction is checked only on the initial
collision, using the objects' relative speed. The required speed is
`max(2, 5.8 + 1.4 × target Resilience - (impactor Hardness - target Hardness))`
metres per second by default. The 2 m/s value is the editable absolute floor;
the hardness gap and target Resilience determine the actual threshold. Wood
against Stone requires at least 14 m/s and Limestone against Stone at least
8 m/s throughout Stone's sampled 4–7 Hardness range. At Stone Hardness 5.5,
these are the exact thresholds. If the impactor is not harder, no speed can
cause destruction outside the similarity range. Each object receives only one
chance per collision, even when both colliders report the contact. Fragments
receive a 0.25 s collision grace period.
Objects with less than 0.002 m³ of world-space mesh volume are protected from
Fracture and Shatter only when their longest object-relative axis is less than
2.5 times the next longest axis. Thin branches, wood fragments and iron bars
remain breakable below that volume. The same shape rule applies to fragment
collisions. Depression and Abrasion remain eligible at any size when their
geometry effects are implemented.

Fracture bisects the current mesh through its bounds center and closes the cut
contours. Stick objects split with a plane perpendicular to their long axis, producing shorter pieces;
other objects use a random cut orientation. Each half gets its own Rigidbody and convex
MeshCollider. Fracture is blocked if the impactor's longest world-space dimension
is half the target's or less, unless the impactor is more than 2 Hardness points
harder; that impact Shatters instead. Shatter has no impactor-size restriction. Full
Shatter makes up to eight capped pieces and removes the source. Wood full
Shatter makes two larger pieces instead of eight; Wood localized Shatter groups
up to 20 adjacent faces into each chip to reduce its fragment count by about
70% or more on connected impact surfaces. Fracture retains its required two
pieces.
Every successful Fracture or Shatter advances a shared fragmentation history.
Each successful break adds a random 0.5–2.5 percentage points to the fragment-size
multiplier, up to a maximum total increase of 30% (1.3 times the original size).
The mass of the other object in the collision adds 0.02% per kilogram, capped
at 20% at 1,000 kg. Both bonuses add to the base size, for a combined maximum
multiplier of 1.5. Collision mass is measured before either object breaks,
including the full mass of a rigidbody assembly, and its bonus applies only to
that impact. Local chip radius, depth and face grouping use this multiplier.
Partial Shatter chips are limited to 0.1 m³ each, including when a large
impactor increases their preferred size.
Fragments, retained
objects and split tree trunks inherit this history; switching between Fracture
and Shatter does not reset it. Failed impacts do not advance it. Size growth is
a preference for all objects and never changes the small compact shape rule.
If larger local chips cannot form valid pieces, the original chip
sizing is retried. Eligible fragments, trunks and branches can continue breaking
after many destructions and at both growth caps. The normal impact, minimum-size
and mesh-validity conditions still apply. Mesh volume and fragment positions
remain conserved.
If the target's longest world-space dimension is at least twice the impactor's,
Shatter instead detaches impacted surface triangles as closed chips and creates
an inset surface and perimeter walls on the retained target. Materials, vertex
colors, per-material renderer color overrides, UVs and physical properties are
inherited by fragments and the retained surface; dynamic mass is distributed
by mesh volume. Neighboring surface triangles form larger chips, reducing the
fragment count in this large-target Shatter case. The impacted region is refined
before chips are detached so a small impact cannot pull off an entire large source face. Disconnected mesh
islands are separated into distinct fragments, each with its own Rigidbody and
a pivot at its volume center. Their original world size and position are
preserved. Before any source is changed, every proposed fragment and retained
surface must form one closed shell: each edge belongs to exactly two oppositely
wound faces, and all faces connect through edges. If the preferred Fracture cut
fails this check, other cut directions are tried. An impact leaves the source
intact if none produces closed pieces. On creation, each fragment receives a small impulse away from the
collision point. Its added speed is 0.08 times the relative collision speed by
default, and the factor is editable per object. Large-target Shatter increases
that outward impulse by 60%. Convex physics hulls approximate
concave visual fragments. Dynamic fragments and dropped objects are checked
against static scenery and corrected if they begin to sink into the ground or
a larger rock.
When a held object breaks completely, the fragment nearest the holding hand
spawns immediately and remains held. A two-hand grip stays in use when the
remaining fragment still requires it.
Wood and burnt-wood fragments receive half the usual outward impulse and one
quarter of its upward component, with initial upward speed capped at 2.5 m/s.
Branches detached by destruction have the same upward-speed cap for their first
half-second of falling.

Destruction is retained for the current scene session. Slicing and Abrasion are
reserved, and Depression geometry is intentionally pending the next design pass.
When Fracture or Shatter actually completes, its effect name appears in a
top-center HUD banner for two seconds. A blocked or below-threshold impact does
not show a banner; a later destruction restarts the two-second timer.
Decorative grass has material properties but retains its existing non-colliding
rendering; it does not generate physical contacts.

Run `Tools > Wyvernland > Validate Physical Destruction` in Unity for regression
checks covering effect selection, capped mesh edges, volume/mass conservation,
fragment bodies/colliders, source replacement and the localized-shatter boundary.

Press P during play to spawn an iron-bar attacker. It pursues the nearest player and uses the same held-strike, waving, fatigue, injury, and knockdown mechanics. Continuous held-strike waving starts slowing after 1.7 seconds and reaches 30% speed at 5 seconds; releasing strike restores full speed.
