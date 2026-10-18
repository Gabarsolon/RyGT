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
