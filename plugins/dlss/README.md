# ryGT DLSS plugin

An optional plugin that lets ryGT scale the game picture with NVIDIA DLSS instead of AMD FSR 1.
It is not part of the ryGT release: NVIDIA's DLSS SDK can't be linked into the GPL renderer, so
DLSS lives in this separate MIT-licensed DLL, and you build and install it yourself.

## What it does

The renderer only has the finished 1920x1080 game picture, with no motion vectors or depth. The
plugin runs DLSS on it with zero motion vectors, a flat depth image, no jitter and its history reset
every frame. That makes it a single-frame upscaler, not the temporal DLSS games ship with. It works
on RTX cards, picks the DLSS quality mode that fits the window size (DLAA when the window is
1920x1080), and falls back to FSR when DLSS is unavailable.

The window resolution is the output resolution, not the game's render resolution. A 2560x1440
window still receives a 1920x1080 picture (75% of the output width and height). Performance and
Balanced render-scale settings are not exposed: choosing another NGX quality mode would not make
the game render fewer pixels. Installing a newer `nvngx_dlss.dll` does not supply the missing
motion vectors, depth or jitter, or enable temporal accumulation.

The plugin caches a successful quality-mode selection for the current input/output dimensions.
Resizing or starting a new device session queries NGX again. Failed selections are retried.
This removes repeated NGX optimal-settings queries from steady-state frames; it does not reduce
the cost of rendering the game's 1080p picture or evaluating DLSS on the GPU.

## Build

You need clang-cl, Visual Studio's C++ libraries, NVIDIA's DLSS SDK
(<https://github.com/NVIDIA/DLSS>, the `include` and `lib\Windows_x86_64\x64` folders) and the
Vulkan headers (shadGT's `externals\vulkan-headers` by default).

```
set DLSS_SDK=C:\path\to\DLSS
build.cmd C:\path\to\output
```

## Install

Next to `GTSport.exe`, make a `dlss` folder holding:

- `ryGT_dlss.dll` from the build;
- `nvngx_dlss.dll` from NVIDIA's DLSS SDK (`lib\Windows_x86_64\rel`) or another DLSS release.

Then tick "Upscale with NVIDIA DLSS" in `RyGT.exe` (the box stays grey until both files are there),
or set `"dlss_enabled": true` in the `GPU` section of `user\custom_configs\CUSA03220.json`. DLSS only
has work to do when the window or screen is larger than 1080p.

`rygt_dlss_api.h` is the interface between the renderer and the plugin; shadGT's
`ryty_gpu/rygt_dlss_api.h` must stay identical.

## Tests

The mode-cache tests need a C++17 compiler and CMake, without the DLSS SDK or an RTX GPU:

```
cmake -S plugins/dlss/tests -B build/dlss-tests
cmake --build build/dlss-tests --config Release
ctest --test-dir build/dlss-tests -C Release --output-on-failure
```

They are also registered by the root project when `BUILD_TESTING` is enabled.
