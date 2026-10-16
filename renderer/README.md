# Renderer changes (shadGT, branch `ryty-gpu`)

The renderer (`libSceVideoOut.prx`) is built from
[shadGT, branch `ryty-gpu`](https://github.com/mlgprorektm8/shadGT/tree/ryty-gpu). Changes made
after shadGT commit `29af7ca6` are kept here as patches, in order (the branch itself includes all of them from
commit `4128c42b`):

| Patch | What it adds |
|---|---|
| `0001-ryGT-frame-generation-...` | AMD FSR 3.1.4 frame generation, shader precompile on the splash screen (with a progress bar), pausing the game while new shaders compile, compile batching, efficiency-core shader compiles, opt-in diagnostics |
| `0002-ryGT-splash-screen-shows-...` | Hotfix: the splash screen and progress bar show while the game waits for the precompile (it was a black screen) |
| `0003-ryGT-Lewis-Hamilton-Time-Trial-...` | GTS-002: unlocks the Lewis Hamilton Time Trial Challenge in the Campaign menu (the add-on's unlock flags came from the shut-down servers); `RYGT_LEWIS=0` turns it off |
| `0004-ryGT-Livery-Editor-offline-GTS-003` | GTS-003: enables the Livery button on the main menu while offline (the menu script greyed it out without the servers; the editor has its own offline mode that saves liveries locally); `RYGT_LIVERY=0` turns it off |
| `0005-ryGT-effects-fix-frame-generation-...` | Dust, smoke and rain no longer show pink and black rectangles (the game's clears of reused effect memory ran as skipped "mask clears"); frame generation starts after the splash screen (it could freeze the precompile); slow-motion shader compiles; freeze and frame-capture diagnostics |
| `0006-ryGT-on-screen-keyboard-offline-livery-...` | On-screen keyboard for text fields, whole-script replacements from `patches\scripts` (GTS-005), custom decals from the `decals` folder (GTS-006) |
| `0007-ryGT-renderer-speed-work-2026-10-15` | Recorder speed-ups (fence writes through cached aliases, texture lookup memo), the decoder's shader search tries the last match first, early-fence deadlock fixes, and the opt-in multithreaded groundwork (per-thread draw state and command rings, buffer stage workers, shared buffer-cache lock) with ceiling and timeline diagnostics |
| `0008-ryGT-direct-memory-renderer-2026-10-16` | The new renderer (launcher option "New renderer", `GPU.direct_renderer`, or `SHADGT_DIRECT=1`): shaders read game memory in place through a GPU page table over imported guest memory instead of copying buffers every draw (Tokyo rain benchmark about 32 -> 43 fps); freed render targets are reused instead of rebuilt (all modes); wet-road reflection (GTS-007) and half-rate mirror (GTS-008) toggles; texture-binding shortcuts; import prototype, ceiling and timing diagnostics |

To build: check out shadGT at `29af7ca6`, apply the patches with
`git am <RyGT>/renderer/shadGT-patches/*.patch`, then build the `ryty_gpu` target as in the main
README (`cmake --preset x64-Clang-Release -DSHADGT_RYTY_GPU=ON`).
