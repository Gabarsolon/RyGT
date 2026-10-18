#ifndef CORE_LIBS_PRX_LIBSCEPAD_PADSTATE_HPP
#define CORE_LIBS_PRX_LIBSCEPAD_PADSTATE_HPP

#include <array>
#include <cstdint>
#include <exception>
#include "SceTypes.hpp"

struct PadTouchPoint {
    bool active = false;
    std::uint16_t x = 0;
    std::uint16_t y = 0;
    std::uint8_t id = 0;

    bool operator==(const PadTouchPoint&) const = default;
};

struct PadInputState {
    std::uint32_t buttons = 0;
    std::array<std::uint8_t, 4> sticks{128, 128, 128, 128};
    std::uint8_t analogButtonsL2 = 0;
    std::uint8_t analogButtonsR2 = 0;
    bool touchLeft = false;
    bool touchRight = false;
    bool hasMotion = false;
    std::array<float, 3> accel{0.0f, 9.80665f, 0.0f};
    std::array<float, 3> gyro{0.0f, 0.0f, 0.0f};
    std::array<PadTouchPoint, 2> touch{};
    std::uint8_t deviceKind = 0;
};

struct PadTriggerRequest {
    bool valid = false;
    std::uint8_t effect[11]{};
    std::uint8_t fallback = 0;
    std::uint32_t mode = 0;
};

struct PadOutputState {
    std::uint32_t sequence = 0;
    std::uint8_t vibrationLarge = 0;
    std::uint8_t vibrationSmall = 0;
    bool lightBarValid = false;
    std::uint8_t lightBar[3]{};
    PadTriggerRequest trigger[2];
    bool triggerTouched = false;
    bool motionEnabled = true;
    int vibrationMode = 0;
};

constexpr int PAD_MAX_PLAYERS = 4;
// Players the game sees (GT Sport's Split Screen is for 2). The input bridge still feeds all
// PAD_MAX_PLAYERS slots; players 3 and 4 are never connected.
constexpr int PAD_ACTIVE_PLAYERS = 2;

// Per player: slot 0 is player 1 (the initial user), slots 1-3 players 2-4.
namespace Pad {
void Initialize();
PadData ReadState(int slot);
bool Connected(int slot);
void SetVibration(int slot, std::uint8_t large, std::uint8_t small);
void SetVibrationMode(int slot, int mode);
void SetLightBar(int slot, bool valid, std::uint8_t r, std::uint8_t g, std::uint8_t b);
void SetTriggerCommand(int slot, int trigger, const std::uint8_t* command);
void ResetOrientation(int slot);
void SetMotionEnabled(int slot, bool enabled);
}

extern "C" void PadPublishInput_nid_postfix(const PadInputState& input);
extern "C" void PadReportInputFailure_nid_postfix(std::exception_ptr error);
extern "C" bool PadFetchOutput_nid_postfix(std::uint32_t* seenSequence, PadOutputState* out);
extern "C" void PadPublishInputSlot_nid_postfix(int slot, const PadInputState& input);
extern "C" bool PadFetchOutputSlot_nid_postfix(int slot, std::uint32_t* seenSequence, PadOutputState* out);
extern "C" void PadSetConnectedSlots_nid_postfix(std::uint32_t mask);

#endif
