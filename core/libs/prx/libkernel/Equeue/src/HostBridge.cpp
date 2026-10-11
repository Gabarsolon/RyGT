// C interface to the event queues for host libraries built with another C++ runtime (the PS4
// video output and Gnm driver from shadGT are built with clang-cl, so they cannot construct
// KernelEqueueEvent, which holds libstdc++ types).
#include <cstdint>
#include <x86intrin.h>
#include "prx/libkernel/Equeue/Equeue.hpp"

namespace {

enum class TriggerMode : int {
    Data = 0,       // event.data = trigger data (PS4 GraphicsCore end-of-pipe events)
    Ps4Display = 1, // PS4 video-out flip/vblank: time, counter and flip argument packed into data
    User = 2,       // event.udata = trigger data
};

void TriggerData(KernelEqueueEvent* e, void* data) {
    e->event.data = static_cast<intptr_t>(reinterpret_cast<uintptr_t>(data));
    e->triggered = true;
}

void TriggerPs4Display(KernelEqueueEvent* e, void* data) {
    e->triggered = true;
    if (data == nullptr) return;
    const auto hint = static_cast<std::uint64_t>(reinterpret_cast<uintptr_t>(data));
    const auto hintEventId = hint & 0xffu;
    if (hintEventId != e->event.ident || e->event.ident == 0xfe) return;
    const auto old = static_cast<std::uint64_t>(e->event.data);
    auto counter = (old >> 12u) & 0xfu;
    if (counter != 0xfu) counter++;
    const auto time = static_cast<std::uint64_t>(__rdtsc());
    e->event.data = static_cast<intptr_t>((time & 0xfffu) | (counter << 12u) | (hint & 0xffffffffffff0000ull));
}

void TriggerUser(KernelEqueueEvent* e, void* data) {
    e->event.udata = data;
    e->triggered = true;
}

void Reset(KernelEqueueEvent* e) {
    e->triggered = false;
    e->event.fflags = 0;
    e->event.data = 0;
}

}

extern "C" {

int RytyEqueueAddEvent_nid_no_patch(KernelEqueue eq, std::uintptr_t ident, std::int16_t filter, std::uint16_t flags,
                                    std::uint32_t fflags, std::intptr_t data, void* udata, int mode, int triggered) {
    KernelEqueueEvent event{};
    event.event.ident = ident;
    event.event.filter = filter;
    event.event.flags = flags;
    event.event.fflags = fflags;
    event.event.data = data;
    event.event.udata = udata;
    event.triggered = triggered != 0;
    switch (static_cast<TriggerMode>(mode)) {
    case TriggerMode::Ps4Display: event.filter.triggerFunc = TriggerPs4Display; break;
    case TriggerMode::User: event.filter.triggerFunc = TriggerUser; break;
    default: event.filter.triggerFunc = TriggerData; break;
    }
    event.filter.resetFunc = Reset;
    return EqueueAddEvent_nid_postfix(eq, event);
}

int RytyEqueueTriggerEvent_nid_no_patch(KernelEqueue eq, std::uintptr_t ident, std::int16_t filter, void* triggerData) {
    return EqueueTriggerEvent_nid_postfix(eq, ident, filter, triggerData);
}

int RytyEqueueDeleteEvent_nid_no_patch(KernelEqueue eq, std::uintptr_t ident, std::int16_t filter) {
    return EqueueDeleteEvent_nid_postfix(eq, ident, filter);
}

int RytyEqueueExists_nid_no_patch(KernelEqueue eq) {
    return EqueuePin_nid_postfix(eq) ? 1 : 0;
}

}
