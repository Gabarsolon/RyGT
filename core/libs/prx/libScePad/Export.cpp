#include <cstddef>
#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <chrono>
#include <thread>

#include "SceTypes.hpp"
#include "prx//libc/include/General.hpp"
#include "prx/libScePad/include/Pad.hpp"
#include "prx/libScePad/include/PadState.hpp"

namespace {

constexpr int PAD_ERROR_DEVICE_NO_HANDLE = static_cast<int>(0x80920008);

constexpr int USER_ID_FIRST = 0x10000000; // libSceUserService's player 1

// Player slot of a user (players 1-2 have user ids 0x10000000-0x10000001); the system
// user's remote port is player 1's.
int SlotOfUser(int userId) {
    if (userId == PAD_USER_ID_SYSTEM) return 0;
    const int slot = userId - USER_ID_FIRST;
    return slot >= 0 && slot < PAD_ACTIVE_PLAYERS ? slot : -1;
}

bool g_opened[PAD_MAX_PLAYERS] = {};

bool ValidPort(int userId, int type, int index) {
    const bool personalPort = type == PAD_PORT_TYPE_STANDARD || type == PAD_PORT_TYPE_SPECIAL;
    const bool systemRemote = userId == PAD_USER_ID_SYSTEM && type == PAD_PORT_TYPE_REMOTE;
    return index == 0 && SlotOfUser(userId) >= 0 && (personalPort || systemRemote);
}

// Handles are player slot + 1 (player 1's is PAD_HANDLE).
int SlotOfHandle(int handle) {
    const int slot = handle - PAD_HANDLE;
    return slot >= 0 && slot < PAD_MAX_PLAYERS && g_opened[slot] ? slot : -1;
}

}

extern "C" {

int APS5_VABI scePadGetFeatureReport() {
 return PAD_OK;
}

int APS5_VABI scePadClose_nid_postfix(int handle) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) {
  return PAD_ERROR_INVALID_HANDLE;
 }
 g_opened[slot] = false;
 return PAD_OK;
}

int APS5_VABI scePadDeviceClassGetExtendedInformation(int handle, PadDeviceClassExtendedInformation* info) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 if (info == nullptr) return PAD_ERROR_INVALID_ARG;
 std::memset(info, 0, sizeof(*info));
 info->deviceClass = PAD_DEVICE_CLASS_STANDARD;
 return PAD_OK;
}

int APS5_VABI scePadDeviceClassParseData(int handle, const PadData* data, PadDeviceClassData* class_data) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 if (data == nullptr || class_data == nullptr) return PAD_ERROR_INVALID_ARG;
 std::memset(class_data, 0, sizeof(*class_data));
 class_data->deviceClass = PAD_DEVICE_CLASS_STANDARD;
 class_data->dataValid = data->connected;
 return PAD_OK;
}

int APS5_VABI scePadGetControllerInformation(int handle, PadControllerInformation* info) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) {
  return PAD_ERROR_INVALID_HANDLE;
 }
 if (info == nullptr) {
  return PAD_ERROR_INVALID_ARG;
 }
 std::memset(info, 0, sizeof(*info));
 info->touchPadInfo.pixelDensity = 44.86f;
 info->touchPadInfo.resolution.x = 1920;
 info->touchPadInfo.resolution.y = 943;
 info->stickInfo.deadZoneLeft = 2;
 info->stickInfo.deadZoneRight = 2;
 info->connectionType = PAD_CONNECTION_TYPE_LOCAL;
 info->connectedCount = 1;
 info->connected = Pad::Connected(slot);
 info->deviceClass = PAD_DEVICE_CLASS_STANDARD;
 return PAD_OK;
}

int APS5_VABI scePadGetHandle(int user_id, int type, int index) {
 if (!ValidPort(user_id, type, index)) return PAD_ERROR_DEVICE_NO_HANDLE;
 const int slot = SlotOfUser(user_id);
 if (!g_opened[slot]) return PAD_ERROR_DEVICE_NO_HANDLE;
 return PAD_HANDLE + slot;
}

int APS5_VABI scePadGetInfo_nid_postfix(PadInfo* info) {
 if (info == nullptr) return PAD_ERROR_INVALID_ARG;
 std::memset(info, 0, sizeof(*info));
 info->maxConnectCount = PAD_ACTIVE_PLAYERS;
 int connected = 0;
 for (int slot = 0; slot < PAD_MAX_PLAYERS; ++slot) connected += Pad::Connected(slot) ? 1 : 0;
 info->connectedCount[0] = static_cast<std::uint8_t>(connected);
 info->connectedCount[1] = 0;
 PadInfo::PadTypeInfo& slot = info->padTypeInfo[0];
 slot.connectPort = 0;
 slot.status = 1;
 slot.deviceType = PAD_DEVICE_TYPE_DUAL_SENSE;
 slot.connectType = PAD_CONNECT_TYPE_BLUETOOTH;
 std::memcpy(slot.bluetoothMacAddress, PAD_BD_ADDRESS, sizeof(slot.bluetoothMacAddress));
 return PAD_OK;
}

int APS5_VABI scePadGetTriggerEffectState(int handle, PadTriggerEffectStateInformation* info) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 if (info == nullptr) return PAD_ERROR_INVALID_ARG;
 std::memset(info, 0, sizeof(*info));
 return PAD_OK;
}

int APS5_VABI scePadGetExtControllerInformation(int handle, void* info) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 if (info == nullptr) return PAD_ERROR_INVALID_ARG;
 std::memset(info, 0, 0x2c);
 PadControllerInformation* base = static_cast<PadControllerInformation*>(info);
 base->touchPadInfo.pixelDensity = 44.86f;
 base->touchPadInfo.resolution.x = 1920;
 base->touchPadInfo.resolution.y = 943;
 base->stickInfo.deadZoneLeft = 2;
 base->stickInfo.deadZoneRight = 2;
 base->connectionType = PAD_CONNECTION_TYPE_LOCAL;
 base->connectedCount = 1;
 base->connected = Pad::Connected(slot);
 base->deviceClass = PAD_DEVICE_CLASS_STANDARD;
 return PAD_OK;
}

int APS5_VABI scePadSetProcessPrivilege(int privilege) {
 (void)privilege;
 return PAD_OK;
}

int APS5_VABI scePadSetParticularMode(bool mode) {
 (void)mode;
 return PAD_OK;
}

int APS5_VABI scePadInit_nid_postfix(void) {
 Pad::Initialize();
 return PAD_OK;
}

int APS5_VABI scePadOpen_nid_postfix(int userId, int type, int index, const void* param) {
 (void)param;
 if (!ValidPort(userId, type, index)) {
  return PAD_ERROR_INVALID_ARG;
 }
 const int slot = SlotOfUser(userId);
 g_opened[slot] = true;
 return PAD_HANDLE + slot;
}

int APS5_VABI scePadReadState(int handle, PadData* data);

int APS5_VABI scePadRead_nid_postfix(int handle, PadData* data, int num) {
    // The title drains the queued states; one current state is reported per call.
    if (data == nullptr || num <= 0) APS5_INVALID_ARG_EX;
    const int result = scePadReadState(handle, data);
    if (result != 0) return result;
    return 1;
}

int APS5_VABI scePadReadState(int handle, PadData* data) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) APS5_INVALID_ARG_EX;
 if (data == nullptr) APS5_INVALID_ARG_EX;

 *data = Pad::ReadState(slot);

 return 0;
}

int APS5_VABI scePadResetLightBar(int handle) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 Pad::SetLightBar(slot, false, 0, 0, 0);
 return PAD_OK;
}

int APS5_VABI scePadResetOrientation(int handle) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 Pad::ResetOrientation(slot);
 return PAD_OK;
}

int APS5_VABI scePadSetAngularVelocityDeadbandState(int handle, bool enable) {
 (void)handle;
 (void)enable;
 NotImplemented_nid_no_patch(__func__);
 return 0;
}

int APS5_VABI scePadSetLightBar(int handle, const PadLightBarParam* param) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 if (param == nullptr) return PAD_ERROR_INVALID_ARG;
 Pad::SetLightBar(slot, true, param->r, param->g, param->b);
 return PAD_OK;
}

int APS5_VABI scePadSetMotionSensorState(int handle, bool enable) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 Pad::SetMotionEnabled(slot, enable);
 return PAD_OK;
}

int APS5_VABI scePadSetTiltCorrectionState(int handle, bool enabled) {
 (void)enabled;
 return SlotOfHandle(handle) < 0 ? PAD_ERROR_INVALID_HANDLE : PAD_OK;
}

int APS5_VABI scePadSetTriggerEffect(int handle, const void* param) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 if (param == nullptr) return PAD_ERROR_INVALID_ARG;
 const std::uint8_t* bytes = static_cast<const std::uint8_t*>(param);
 const std::uint8_t mask = bytes[0];
 if ((mask & ~0x3u) != 0) {
  return PAD_ERROR_INVALID_ARG;
 }
 std::uint32_t mode[2] = {};
 for (int trigger = 0; trigger < 2; ++trigger) std::memcpy(&mode[trigger], bytes + 8 + 56 * trigger, 4);
 if (mode[0] > 6 || mode[1] > 6) {
  return PAD_ERROR_INVALID_ARG;
 }
 for (int trigger = 0; trigger < 2; ++trigger) {
  if ((mask & (1u << trigger)) != 0) Pad::SetTriggerCommand(slot, trigger, bytes + 8 + 56 * trigger);
 }
 return PAD_OK;
}

int APS5_VABI scePadSetVibration(int handle, const PadVibrationParam* param) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 if (param == nullptr) return PAD_ERROR_INVALID_ARG;
 Pad::SetVibration(slot, param->large_motor, param->small_motor);
 return PAD_OK;
}

int APS5_VABI scePadSetVibrationMode(int handle, int mode) {
 const int slot = SlotOfHandle(handle);
 if (slot < 0) return PAD_ERROR_INVALID_HANDLE;
 if (mode != 0 && mode != 1) return PAD_ERROR_INVALID_ARG;
 Pad::SetVibrationMode(slot, mode);
 return PAD_OK;
}

int APS5_VABI scePadSetVibrationTriggerEffectWeakWhileEmbeddedMicInUse(bool enabled) {
 (void)enabled;
 return PAD_OK;
}

}

// Controller extensions unsupported by this runtime.
extern "C" {
int APS5_VABI scePadClose() { return PAD_OK; }
int APS5_VABI scePadConnectPort() { return PAD_OK; }
int APS5_VABI scePadDeviceOpen() { return PAD_OK; }
int APS5_VABI scePadDisableTriggerEffect() { return PAD_OK; }
int APS5_VABI scePadDisableVibration() { return PAD_OK; }
int APS5_VABI scePadDisconnectDevice() { return PAD_OK; }
int APS5_VABI scePadDisconnectPort() { return PAD_OK; }
int APS5_VABI scePadEnableAutoDetect() { return PAD_OK; }
int APS5_VABI scePadEnableExtensionPort() { return PAD_OK; }
int APS5_VABI scePadEnableSpecificDeviceClass() { return PAD_OK; }
int APS5_VABI scePadEnableUsbConnection() { return PAD_OK; }
int APS5_VABI scePadGetActiveTime() { return PAD_OK; }
int APS5_VABI scePadGetBluetoothAddress() { return PAD_OK; }
int APS5_VABI scePadGetCapability() { return PAD_OK; }
int APS5_VABI scePadGetDataInternal() { return PAD_OK; }
int APS5_VABI scePadGetDeviceId() { return PAD_OK; }
int APS5_VABI scePadGetDeviceInfo() { return PAD_OK; }
int APS5_VABI scePadGetExtensionUnitInfo() { return PAD_OK; }
int APS5_VABI scePadGetFeatureReport2() { return PAD_OK; }
int APS5_VABI scePadGetIdleCount() { return PAD_OK; }
int APS5_VABI scePadGetInfo() { return PAD_OK; }
int APS5_VABI scePadGetInfoByPortType() { return PAD_OK; }
int APS5_VABI scePadGetLicenseControllerInformation() { return PAD_OK; }
int APS5_VABI scePadGetMotionSensorPosition() { return PAD_OK; }
int APS5_VABI scePadGetMotionTimerUnit() { return PAD_OK; }
int APS5_VABI scePadGetSphereRadius() { return PAD_OK; }
int APS5_VABI scePadGetVersionInfo() { return PAD_OK; }
int APS5_VABI scePadInit() { return PAD_OK; }
int APS5_VABI scePadIsBlasterConnected() { return PAD_OK; }
int APS5_VABI scePadIsDS4Connected() { return PAD_OK; }
int APS5_VABI scePadIsLightBarBaseBrightnessControllable() { return PAD_OK; }
int APS5_VABI scePadIsMoveConnected() { return PAD_OK; }
int APS5_VABI scePadIsMoveReproductionModel() { return PAD_OK; }
int APS5_VABI scePadIsValidHandle() { return PAD_OK; }
int APS5_VABI scePadLastLotusButtonData() { return PAD_OK; }
int APS5_VABI scePadMbusInit() { return PAD_OK; }
int APS5_VABI scePadMbusTerm() { return PAD_OK; }
int APS5_VABI scePadOpen() { return PAD_OK; }
int APS5_VABI scePadOpenExt() { return PAD_OK; }
int APS5_VABI scePadOpenExt2() { return PAD_OK; }
int APS5_VABI scePadOutputReport() { return PAD_OK; }
int APS5_VABI scePadRead() { return PAD_OK; }
int APS5_VABI scePadReadBlasterForTracker() { return PAD_OK; }
int APS5_VABI scePadReadExt() { return PAD_OK; }
int APS5_VABI scePadReadForTracker() { return PAD_OK; }
int APS5_VABI scePadReadHistory() { return PAD_OK; }
int APS5_VABI scePadReadStateExt() { return PAD_OK; }
int APS5_VABI scePadRemoteControlInsertData() { return PAD_OK; }
int APS5_VABI scePadResetLightBarAll() { return PAD_OK; }
int APS5_VABI scePadResetLightBarAllByPortType() { return PAD_OK; }
int APS5_VABI scePadResetOrientationForTracker() { return PAD_OK; }
int APS5_VABI scePadSetAngularVelocityBiasCorrectionState() { return PAD_OK; }
int APS5_VABI scePadSetAutoPowerOffCount() { return PAD_OK; }
int APS5_VABI scePadSetButtonRemappingInfo() { return PAD_OK; }
int APS5_VABI scePadSetConnection() { return PAD_OK; }
int APS5_VABI scePadSetExtensionReport() { return PAD_OK; }
int APS5_VABI scePadSetFeatureReport() { return PAD_OK; }
int APS5_VABI scePadSetFeatureReport2() { return PAD_OK; }
int APS5_VABI scePadSetForceIntercepted() { return PAD_OK; }
int APS5_VABI scePadSetLightBarBaseBrightness() { return PAD_OK; }
int APS5_VABI scePadSetLightBarBlinking() { return PAD_OK; }
int APS5_VABI scePadSetLightBarForTracker() { return PAD_OK; }
int APS5_VABI scePadSetLoginUserNumber() { return PAD_OK; }
int APS5_VABI scePadSetProcessFocus() { return PAD_OK; }
int APS5_VABI scePadSetProcessPrivilegeOfButtonRemapping() { return PAD_OK; }
int APS5_VABI scePadSetShareButtonMaskForRemotePlay() { return PAD_OK; }
int APS5_VABI scePadSetUserColor() { return PAD_OK; }
int APS5_VABI scePadSetUserNumber() { return PAD_OK; }
int APS5_VABI scePadSetVibrationForce() { return PAD_OK; }
int APS5_VABI scePadSetVrTrackingMode() { return PAD_OK; }
int APS5_VABI scePadShareOutputData() { return PAD_OK; }
int APS5_VABI scePadStartRecording() { return PAD_OK; }
int APS5_VABI scePadStopRecording() { return PAD_OK; }
int APS5_VABI scePadSwitchConnection() { return PAD_OK; }
int APS5_VABI scePadTrackerCalibrate() { return PAD_OK; }
int APS5_VABI scePadTrackerGetWorkingMemorySize() { return PAD_OK; }
int APS5_VABI scePadTrackerInit() { return PAD_OK; }
int APS5_VABI scePadTrackerReadState() { return PAD_OK; }
int APS5_VABI scePadTrackerTerm() { return PAD_OK; }
int APS5_VABI scePadTrackerUpdate() { return PAD_OK; }
int APS5_VABI scePadVertualDeviceAddDevice() { return PAD_OK; }
int APS5_VABI scePadVirtualDeviceAddDevice() { return PAD_OK; }
int APS5_VABI scePadVirtualDeviceDeleteDevice() { return PAD_OK; }
int APS5_VABI scePadVirtualDeviceDisableButtonRemapping() { return PAD_OK; }
int APS5_VABI scePadVirtualDeviceGetRemoteSetting() { return PAD_OK; }
int APS5_VABI scePadVirtualDeviceInsertData() { return PAD_OK; }
int APS5_VABI scePadVrControllerGetDeviceInformation() { return PAD_OK; }
int APS5_VABI scePadVrControllerGetTriggerEffectState() { return PAD_OK; }
int APS5_VABI scePadVrControllerGetTriggerEffectStates() { return PAD_OK; }
int APS5_VABI scePadVrControllerRead() { return PAD_OK; }
int APS5_VABI scePadVrControllerReadCombinedState() { return PAD_OK; }
int APS5_VABI scePadVrControllerReadState() { return PAD_OK; }
int APS5_VABI scePadVrControllerSetTriggerEffect() { return PAD_OK; }
int APS5_VABI scePadVrControllerSetTriggerEffects() { return PAD_OK; }
int APS5_VABI scePadVrControllerSetVibration() { return PAD_OK; }
int APS5_VABI scePadVrControllerSetVibrationMode() { return PAD_OK; }
}

