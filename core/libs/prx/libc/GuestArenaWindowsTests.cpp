#include "prx/libc/include/GuestArena.hpp"
#include "prx/libc/include/WindowsMappings.hpp"
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <stdexcept>

namespace {
void Require(bool ok, const char* message) {
    if (!ok) throw std::runtime_error(message);
}

MEMORY_BASIC_INFORMATION Query(const void* address) {
    MEMORY_BASIC_INFORMATION info{};
    Require(VirtualQuery(address, &info, sizeof(info)) == sizeof(info), "VirtualQuery failed");
    return info;
}
}

int main() {
    using namespace GuestArena;
    constexpr std::size_t Page = 0x4000;
    // Stand in for NVIDIA's interop reservation after libc's static initialization.
    void* driverBase = nullptr;
    constexpr std::size_t DriverBytes = 0x4000000000ull;
    try {
        for (std::uintptr_t candidate = 0x1100000000ull; candidate < 0xd000000000ull; candidate += 0x1000000000ull) {
            driverBase = VirtualAlloc(reinterpret_cast<void*>(candidate), DriverBytes, MEM_RESERVE, PAGE_NOACCESS);
            if (driverBase) break;
        }
        Require(driverBase != nullptr, "guest arena left no room for a driver reservation");

        auto* startup = reinterpret_cast<std::uint8_t*>(0x1020000000ull);
        Require(Query(startup).State == MEM_RESERVE, "startup fixed mapping window was not protected");
        GuestArenaMarkUsed_nid_postfix(startup, 0x200000);
        GuestArenaCommit_nid_postfix(startup, 0x200000, PAGE_READWRITE, Page);
        startup[0] = 7;
        GuestArenaReset_nid_postfix(startup, 0x200000);
        GuestArenaRelease_nid_postfix(startup, 0x200000);

        auto* first = static_cast<std::uint8_t*>(GuestArenaAllocate_nid_postfix(Page, Page));
        auto* second = static_cast<std::uint8_t*>(GuestArenaAllocate_nid_postfix(Page, Page));
        Require(second == first + Page, "sub-granularity guest allocations stopped packing together");
        GuestArenaCommit_nid_postfix(first, Page, PAGE_READWRITE, Page);
        GuestArenaCommit_nid_postfix(second, Page, PAGE_READWRITE, Page);
        first[0] = 11;
        second[0] = 22;
        GuestArenaReset_nid_postfix(first, Page);
        GuestArenaRelease_nid_postfix(first, Page);
        Require(second[0] == 22, "reset damaged a neighboring guest mapping");
        Require(GuestArenaAllocate_nid_postfix(Page, Page) == first, "released guest range was not reused");
        GuestArenaCommit_nid_postfix(first, Page, PAGE_READWRITE, Page);
        first[0] = 33;

        const auto driverAddress = reinterpret_cast<std::uintptr_t>(driverBase);
        auto* large = WindowsMappings::Get().FindAndReserve(driverAddress - 0x10000, driverAddress + DriverBytes + 0x200000000ull,
                                                          0x200000000ull, 0x10000);
        Require(large && reinterpret_cast<std::uintptr_t>(large) >= driverAddress + DriverBytes,
                "automatic guest allocation did not skip the driver's reservation");
        Require(Query(driverBase).AllocationBase == driverBase, "driver reservation was changed");

        bool rejected = false;
        try { GuestArenaMarkUsed_nid_postfix(driverBase, Page); }
        catch (const std::runtime_error&) { rejected = true; }
        Require(rejected, "fixed guest allocation accepted a host reservation");
        rejected = false;
        try { GuestArenaReset_nid_postfix(driverBase, Page); }
        catch (const std::runtime_error&) { rejected = true; }
        Require(rejected && Query(driverBase).AllocationBase == driverBase,
                "guest reset changed a host reservation");

        auto* fixed = reinterpret_cast<std::uint8_t*>(0xf3ffe00000ull);
        GuestArenaMarkUsed_nid_postfix(fixed, 2 * Page);
        GuestArenaCommit_nid_postfix(fixed, 2 * Page, PAGE_READWRITE, Page);
        fixed[0] = 44;
        fixed[Page] = 55;
        GuestArenaReset_nid_postfix(fixed, 2 * Page);
        HANDLE section = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_EXECUTE_READWRITE, 0, 2 * Page, nullptr);
        Require(section != nullptr, "could not create guest backing");
        GuestArenaMap_nid_postfix(fixed, 2 * Page, section, 0, PAGE_READWRITE);
        fixed[0] = 66;
        fixed[Page] = 77;
        auto* alias = static_cast<std::uint8_t*>(GuestArenaMapAlias_nid_postfix(reinterpret_cast<std::uintptr_t>(fixed), 2 * Page));
        Require(alias[Page] == 77, "guest alias did not share the backing");
        alias[Page] = 88;
        GuestArenaReset_nid_postfix(fixed, Page);
        Require(fixed[Page] == 88, "partial unmap damaged the rest of a shared view");
        GuestArenaCommit_nid_postfix(fixed, Page, PAGE_READWRITE, Page);
        fixed[0] = 99;
        Require(fixed[Page] == 88, "partial recommit damaged shared backing");
        GuestArenaUnmapAlias_nid_postfix(alias);
        GuestArenaReset_nid_postfix(fixed, 2 * Page);
        GuestArenaRelease_nid_postfix(fixed, 2 * Page);
        CloseHandle(section);
        VirtualFree(driverBase, 0, MEM_RELEASE);
        std::puts("PASS: sparse arena, host collisions, reuse, fixed mappings, partial unmap, shared aliases");
        return 0;
    } catch (const std::exception& error) {
        std::fprintf(stderr, "FAIL: %s (Windows error %lu)\n", error.what(), GetLastError());
        return 1;
    }
}
