#pragma once
#include <cstdint>
enum GpuReadbacksMode : std::uint32_t { Disabled, Precise };
struct TestSettings { std::uint32_t GetReadbacksMode() const { return Disabled; } };
inline TestSettings EmulatorSettings;
