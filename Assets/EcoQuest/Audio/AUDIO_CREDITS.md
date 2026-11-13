# EcoQuest Audio Credits & Inventory

All original EcoQuest audio clips listed below were generated specifically for **EcoQuest** using [`Assets/EcoQuest/Audio/Tools/generate_ecoquest_audio.py`](Tools/generate_ecoquest_audio.py) (seeded deterministic synthesis + `ffmpeg` Vorbis encoding) and are dedicated to the public domain under **CC0 1.0 Universal (CC0 1.0)**.

## Music Loops (`Assets/EcoQuest/Audio/Music/`, 44.1 kHz Stereo OGG)

| File | Duration | Seam / Loop Diff | Target Scene | Musical Mood |
| --- | --- | --- | --- | --- |
| `bgm_main_menu.ogg` | 21.82s | 0.0014 (seamless) | `Main Menu` | Warm wooden marimba, soft pads, gentle sub-bass (88 BPM, C major) |
| `bgm_forest.ogg` | 31.30s | 0.0035 (seamless) | `Forest Scene` | Relaxed woodland kalimba & warm pad progression (92 BPM, F major) |
| `bgm_city.ogg` | 17.78s | 0.0006 (seamless) | `City Scene` | Upbeat friendly kalimba + warm city groove (108 BPM, G major) |

## Ambience Loops (`Assets/EcoQuest/Audio/Ambience/`, 44.1 kHz Stereo OGG)

| File | Duration | Seam / Loop Diff | Target Scene | Bed Profile |
| --- | --- | --- | --- | --- |
| `amb_forest.ogg` | 16.00s | 0.0385 (seamless) | `Forest Scene` | Gentle leaf breeze + subtle distant woodland bird chirps |
| `amb_city.ogg` | 16.00s | 0.0331 (seamless) | `City Scene` | Soft outdoor urban air + distant gentle sparrows |

## UI & HUD SFX (`Assets/EcoQuest/Audio/UI/`, 44.1 kHz Mono WAV)

| File | Duration | Used For |
| --- | --- | --- |
| `ui_button_hover.wav` | 0.05s | Subtle button hover feedback |
| `ui_button_click.wav` | 0.09s | Tactile wooden UI button press |
| `ui_slider_tick.wav` | 0.03s | Settings slider drag tick (rate-limited) |
| `ui_modal_open.wav` | 0.22s | Opening Settings, EcoShop, Inventory, Dialogue, Quiz intro |
| `ui_modal_close.wav` | 0.22s | Closing modals, Settings, Pause, Dialogue |
| `ui_panel_whoosh.wav` | 0.16s | Quest panel & Fullscreen Minimap toggle |
| `ui_dialogue_blip.wav` | 0.06s | Soft marimba typewriter blip during NPC dialogue |
| `ui_notification.wav` | 0.44s | Explorer Gate / Wrong Trash / general alert notifications |
| `ui_tutorial_step.wav` | 0.70s | Tutorial step completion sparkle |
| `ui_hunger_warning.wav` | 0.42s | Low hunger / starving threshold warning chime |
| `ui_shop_buy.wav` | 0.46s | EcoShop purchase success |
| `ui_shop_error.wav` | 0.26s | EcoShop insufficient coins / locked feedback |
| `ui_item_use.wav` | 0.36s | Inventory consumable use boost |

## Gameplay SFX (`Assets/EcoQuest/Audio/Gameplay/`, 44.1 kHz Mono WAV)

| File | Duration | Used For |
| --- | --- | --- |
| `sfx_footstep_grass_01..04.wav` | 0.13s | Forest ground footstep variations (with pitch jitter) |
| `sfx_footstep_city_01..04.wav` | 0.11s | City pavement footstep variations (with pitch jitter) |
| `sfx_jump.wav` | 0.16s | Player jump takeoff |
| `sfx_land.wav` | 0.16s | Player landing thud |
| `sfx_trash_pickup.wav` | 0.14s | Picking up litter/trash |
| `sfx_trash_drop.wav` | 0.13s | Dropping held trash |
| `sfx_trash_correct.wav` | 0.46s | Correct trash bin deposit |
| `sfx_trash_wrong.wav` | 0.34s | Wrong trash bin deposit / litter rejection |
| `sfx_quiz_correct.wav` | 0.50s | Pre/Post/Bonus quiz correct answer |
| `sfx_quiz_wrong.wav` | 0.36s | Pre/Post/Bonus quiz incorrect answer |
| `sfx_coin_earn.wav` | 0.35s | Coin reward grant |
| `sfx_quest_complete.wav` | 1.15s | Quest / Bonus quest completion fanfare |
| `sfx_litter_throw.wav` | 0.18s | Litterbug throw arc launch (3D) |
| `sfx_litter_land.wav` | 0.14s | Thrown trash landing bounce (3D) |
| `sfx_loading_ready.wav` | 0.56s | Async loading ready chime |

## City Traffic 3D SFX (`Assets/EcoQuest/Audio/Traffic/`, 22.05 kHz Mono WAV)

| File | Duration | Used For |
| --- | --- | --- |
| `sfx_car_engine_hum.wav` | 2.00s | Looping vehicle engine purr (seamless, fixes broken serialized null clip) |
| `sfx_car_horn_friendly.wav` | 0.34s | Friendly double car horn |
| `sfx_train_rumble.wav` | 2.00s | Looping train track cadence rumble (seamless) |
| `sfx_train_horn.wav` | 0.85s | Warm chorded train horn |
| `sfx_rail_crossing_bell.wav` | 0.42s | Rail crossing safety bell |

---

*Note: Third-party `Assets/Plugins/Loading Screen/` example audio files remain untouched and unused by EcoQuest scenes.*
