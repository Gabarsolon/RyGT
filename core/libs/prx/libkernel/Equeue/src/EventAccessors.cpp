#include <cstdint>
#include <cstddef>
#include <stdexcept>
#include "SceTypes.hpp"
#include "prx/libc/include/General.hpp"

namespace {

const KernelEvent& Checked(const KernelEvent* ev) {
    if (ev == nullptr) throw std::invalid_argument("Null kernel event");
    return *ev;
}

}

extern "C" {

intptr_t APS5_VABI sceKernelGetEventData(const KernelEvent* ev) {
    return Checked(ev).data;
}

int APS5_VABI sceKernelGetEventError(const KernelEvent* ev) {
    // kqueue reports errors through EV_ERROR (0x4000) with the error code in data.
    const auto& event = Checked(ev);
    return (event.flags & 0x4000) != 0 ? static_cast<int>(event.data) : 0;
}

intptr_t APS5_VABI sceKernelGetEventFflags(const KernelEvent* ev) {
    return Checked(ev).fflags;
}

int APS5_VABI sceKernelGetEventFilter(const KernelEvent* ev) {
    return Checked(ev).filter;
}

uintptr_t APS5_VABI sceKernelGetEventId(const KernelEvent* ev) {
    return Checked(ev).ident;
}

void* APS5_VABI sceKernelGetEventUserData(const KernelEvent* ev) {
    return Checked(ev).udata;
}

}
