#include <cstddef>
#include <cstdint>
#include "SceTypes.hpp"
#include "prx/libc/include/General.hpp"
#include <atomic>
#include <cstring>
#include <cstdlib>
#include <string>
#include "prx/libSceUserService/UserService.hpp"
#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

namespace {

std::size_t Utf8PrefixLength(const char* text, std::size_t max_bytes) {
 const std::size_t length = std::strlen(text);
 std::size_t offset = 0;
 while (offset < length && offset < max_bytes) {
  const unsigned char lead = static_cast<unsigned char>(text[offset]);
  std::size_t width = 1;
  if ((lead & 0xe0) == 0xc0) width = 2;
  else if ((lead & 0xf0) == 0xe0) width = 3;
  else if ((lead & 0xf8) == 0xf0) width = 4;
  if (offset + width > length || offset + width > max_bytes) break;
  bool valid = true;
  for (std::size_t i = 1; i < width; ++i) {
   if ((static_cast<unsigned char>(text[offset + i]) & 0xc0) != 0x80) {
    valid = false;
    break;
   }
  }
  offset += valid ? width : 1;
 }
 return offset;
}

std::string ConfiguredUserName() {
#ifdef _WIN32
 const wchar_t* configured_name = _wgetenv(L"RYGT_USER_NAME");
 if (configured_name == nullptr || configured_name[0] == L'\0') return "Player";
 const int byte_count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, configured_name, -1, nullptr, 0, nullptr, nullptr);
 if (byte_count <= 1) return "Player";
 std::string result(static_cast<std::size_t>(byte_count), '\0');
 if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, configured_name, -1, result.data(), byte_count, nullptr, nullptr) != byte_count)
  return "Player";
 result.resize(static_cast<std::size_t>(byte_count - 1));
 return result;
#else
 const char* configured_name = std::getenv(USER_SERVICE_USER_NAME_ENV);
 return configured_name == nullptr || configured_name[0] == '\0' ? "Player" : configured_name;
#endif
}

}

extern "C" {

int APS5_VABI sceUserServiceGetAccessibilityChatTranscription(int user_id, int32_t* chat_transcription) {
 if (chat_transcription == nullptr || user_id != USER_SERVICE_INITIAL_USER_ID) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 *chat_transcription = 0;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetAccessibilityPressAndHoldDelay(int user_id, int32_t* press_and_hold_delay) {
 if (press_and_hold_delay == nullptr || user_id != USER_SERVICE_INITIAL_USER_ID) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 *press_and_hold_delay = 0;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetAccessibilityTriggerEffect(int user_id, int32_t* trigger_effect) {
 if (trigger_effect == nullptr || user_id != USER_SERVICE_INITIAL_USER_ID) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 *trigger_effect = 0;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetAccessibilityVibration(int user_id, int32_t* vibration) {
 if (vibration == nullptr || user_id != USER_SERVICE_INITIAL_USER_ID) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 *vibration = 0;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetAccessibilityZoomEnabled(int user_id, int32_t* zoom_enabled) {
 if (zoom_enabled == nullptr || user_id != USER_SERVICE_INITIAL_USER_ID) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 *zoom_enabled = 0;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetAgeLevel(int user_id, uint32_t* age_level) {
 (void)user_id;
 (void)age_level;
 NotImplemented_nid_no_patch(__func__);
 return 0;
}

// The initial user is reported as logging in once; afterwards there are no user events.
int APS5_VABI sceUserServiceGetEvent(SceUserServiceEvent* event) {
 if (event == nullptr) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 static std::atomic<bool> loginReported{false};
 if (loginReported.exchange(true)) {
  return USER_SERVICE_ERROR_NO_EVENT;
 }
 constexpr std::uint32_t EventTypeLogin = 0;
 event->event_type = EventTypeLogin;
 event->user_id = USER_SERVICE_INITIAL_USER_ID;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetGamePresets(int user_id, UserServiceGamePresets* presets) {
 // System-wide game presets (difficulty, view inversion, subtitles, audio language) of the user's
 // profile. A fresh console has none set: every field is 0, "not specified", and the title falls
 // back to its own defaults. this_size is the caller's.
 if (presets == nullptr || user_id != USER_SERVICE_INITIAL_USER_ID) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 presets->difficulty = 0;
 presets->priority = 0;
 presets->invert_vertical_view_for_1st_person_view = 0;
 presets->invert_horizontal_view_for_1st_person_view = 0;
 presets->invert_vertical_view_for_3rd_person_view = 0;
 presets->invert_horizontal_view_for_3rd_person_view = 0;
 presets->display_sub_titles = 0;
 presets->audio_language = 0;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetInitialUser(int* user_id) {
 if (user_id == nullptr) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 *user_id = USER_SERVICE_INITIAL_USER_ID;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetLoginUserIdList(UserServiceLoginUserIdList* user_id_list) {
 if (user_id_list == nullptr) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 user_id_list->user_id[0] = USER_SERVICE_INITIAL_USER_ID;
 user_id_list->user_id[1] = USER_SERVICE_USER_ID_INVALID;
 user_id_list->user_id[2] = USER_SERVICE_USER_ID_INVALID;
 user_id_list->user_id[3] = USER_SERVICE_USER_ID_INVALID;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetUserName(int user_id, char* name, size_t size) {
 if (name == nullptr || user_id != USER_SERVICE_INITIAL_USER_ID) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 if (size < USER_SERVICE_MAX_USER_NAME_LENGTH + 1) {
  return USER_SERVICE_ERROR_BUFFER_TOO_SHORT;
 }
 const std::string configured_name = ConfiguredUserName();
 const size_t name_length = Utf8PrefixLength(configured_name.c_str(), USER_SERVICE_MAX_USER_NAME_LENGTH);
 std::memcpy(name, configured_name.c_str(), name_length);
 name[name_length] = '\0';
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceGetUserNumber(int user_id, int32_t* number) {
 if (number == nullptr || user_id != USER_SERVICE_INITIAL_USER_ID) {
  return USER_SERVICE_ERROR_INVALID_ARGUMENT;
 }
 *number = 1;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceInitialize(const void* params) {
 (void)params;
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceInitialize2(void) {
 return USER_SERVICE_OK;
}

int APS5_VABI sceUserServiceTerminate(void) {
 return USER_SERVICE_OK;
}

// No PSN account exists, so the platform privacy setting reports the feature as not permitted.
int APS5_VABI sceUserServiceGetPlatformPrivacyWs1(int32_t user_id, int32_t* value) {
    (void)user_id;
    if (!value) return USER_SERVICE_ERROR_INVALID_ARGUMENT;
    *value = 0;
    return 0;
}

}
