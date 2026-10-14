# Renderer changes (shadGT, branch `ryty-gpu`)

The renderer (`libSceVideoOut.prx`) is built from
[shadGT, branch `ryty-gpu`](https://github.com/mlgprorektm8/shadGT/tree/ryty-gpu). Changes made
after shadGT commit `29af7ca6` are kept here as patches, in order:

| Patch | What it adds |
|---|---|
| `0001-ryGT-frame-generation-...` | AMD FSR 3.1.4 frame generation, shader precompile on the splash screen (with a progress bar), pausing the game while new shaders compile, compile batching, efficiency-core shader compiles, opt-in diagnostics |
| `0002-ryGT-splash-screen-shows-...` | Hotfix: the splash screen and progress bar show while the game waits for the precompile (it was a black screen) |
| `0003-ryGT-Lewis-Hamilton-Time-Trial-...` | GTS-002: unlocks the Lewis Hamilton Time Trial Challenge in the Campaign menu (the add-on's unlock flags came from the shut-down servers); `RYGT_LEWIS=0` turns it off |

To build: check out shadGT at `29af7ca6`, apply the patches with
`git am <RyGT>/renderer/shadGT-patches/*.patch`, then build the `ryty_gpu` target as in the main
README (`cmake --preset x64-Clang-Release -DSHADGT_RYTY_GPU=ON`).
