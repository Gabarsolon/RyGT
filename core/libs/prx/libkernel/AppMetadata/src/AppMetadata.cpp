#include "prx/libkernel/AppMetadata/include/AppMetadata.hpp"
#include "prx/libkernel/AppMetadata/include/ParamJsonParser.hpp"
#include "prx/libc/include/General.hpp"

#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

bool g_titleLoaded = false;
bool g_iconAttempted = false;
bool g_iconLoaded = false;
char g_title[128] = {};
char g_titleId[12] = {};
std::uint64_t g_downloadDataSizeMiB = 0;
std::vector<std::uint8_t> g_iconBytes;

constexpr const char* AppMetadataParamJsonGuestPath = "/app0/sce_sys/param.json";
constexpr const char* AppMetadataParamSfoGuestPath = "/app0/sce_sys/param.sfo";
constexpr const char* AppMetadataIconGuestPath = "/app0/sce_sys/icon0.png";

void copyToFixedBuffer(char* destination, std::size_t destinationSize, const std::string& source) {
    if (source.empty()) throw std::runtime_error("source string is empty");
    if (source.size() + 1 > destinationSize) throw std::length_error("source string does not fit into destination buffer");
    std::memcpy(destination, source.c_str(), source.size() + 1);
}

// PS4 titles describe themselves in param.sfo: a header, then 16-byte entries pointing into a key
// table and a data table. Strings are UTF-8 (format 0x0204), integers 32-bit (format 0x0404).
ParsedParamJson parseParamSfo(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file.is_open()) throw std::runtime_error("failed to open param.sfo");
    const std::vector<char> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    const auto read32 = [&](std::size_t offset) {
        if (offset + 4 > bytes.size()) throw std::runtime_error("param.sfo is truncated");
        std::uint32_t value;
        std::memcpy(&value, bytes.data() + offset, sizeof(value));
        return value;
    };
    if (bytes.size() < 20 || std::memcmp(bytes.data(), "\0PSF", 4) != 0) throw std::runtime_error("param.sfo has no PSF header");
    const std::uint32_t keyTable = read32(8);
    const std::uint32_t dataTable = read32(12);
    const std::uint32_t entries = read32(16);
    ParsedParamJson parsed;
    for (std::uint32_t index = 0; index < entries; ++index) {
        const std::size_t entry = 20 + std::size_t{index} * 16;
        const std::uint32_t keyAndFormat = read32(entry);
        const std::size_t keyOffset = keyTable + (keyAndFormat & 0xFFFFu);
        const std::uint32_t format = keyAndFormat >> 16;
        const std::uint32_t length = read32(entry + 4);
        const std::size_t dataOffset = dataTable + read32(entry + 12);
        if (keyOffset >= bytes.size() || dataOffset + length > bytes.size()) throw std::runtime_error("param.sfo entry is out of range");
        const std::string key(bytes.data() + keyOffset);
        if (format == 0x0404 && key == "DOWNLOAD_DATA_SIZE") {
            parsed.downloadDataSizeMiB = read32(dataOffset);
        } else if (format == 0x0204 && (key == "TITLE" || key == "TITLE_ID")) {
            std::string value(bytes.data() + dataOffset, length);
            value.resize(std::strlen(value.c_str()));
            (key == "TITLE" ? parsed.title : parsed.titleId) = value;
        }
    }
    return parsed;
}

void ensureTitleLoaded() {
    if (g_titleLoaded) return;
    const auto resolvedPath = ResolvePath_nid_no_patch(AppMetadataParamJsonGuestPath);
    const auto sfoPath = ResolvePath_nid_no_patch(AppMetadataParamSfoGuestPath);
    if (!std::filesystem::exists(resolvedPath) && !std::filesystem::exists(sfoPath)) throw std::runtime_error("neither param.json nor param.sfo found");
    const auto parsed = std::filesystem::exists(resolvedPath) ? parseParamJson(resolvedPath) : parseParamSfo(sfoPath);
    copyToFixedBuffer(g_title, sizeof(g_title), parsed.title);
    copyToFixedBuffer(g_titleId, sizeof(g_titleId), parsed.titleId);
    g_downloadDataSizeMiB = parsed.downloadDataSizeMiB;
    g_titleLoaded = true;
}

void ensureIconLoaded() {
    if (g_iconAttempted) return;
    g_iconAttempted = true;
    const auto resolvedPath = ResolvePath_nid_no_patch(AppMetadataIconGuestPath);
    if (!std::filesystem::exists(resolvedPath)) return;
    std::ifstream file(resolvedPath, std::ios::binary);
    if (!file.is_open()) throw std::runtime_error("failed to open icon0.png");
    file.seekg(0, std::ios::end);
    const auto fileSize = file.tellg();
    file.seekg(0, std::ios::beg);
    std::vector<std::uint8_t> bytes(static_cast<std::size_t>(fileSize));
    if (!bytes.empty()) file.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (bytes.empty()) throw std::runtime_error("icon0.png is empty");
    constexpr std::uint8_t PngSignature[8] = {0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A};
    if (bytes.size() < 8 || std::memcmp(bytes.data(), PngSignature, sizeof(PngSignature)) != 0) throw std::runtime_error("icon0.png is not a valid PNG file");
    g_iconBytes = std::move(bytes);
    g_iconLoaded = true;
}

}

extern "C" {

AppTitle GetAppTitle_nid_postfix() {
    ensureTitleLoaded();
    AppTitle result{};
    std::memcpy(result.value, g_title, sizeof(result.value));
    return result;
}

std::uint64_t GetAppDownloadDataSizeMiB_nid_postfix() {
    ensureTitleLoaded();
    return g_downloadDataSizeMiB;
}

AppTitleId GetAppTitleId_nid_postfix() {
    ensureTitleLoaded();
    AppTitleId result{};
    std::memcpy(result.value, g_titleId, sizeof(result.value));
    return result;
}

bool HasAppIcon_nid_postfix() {
    ensureIconLoaded();
    return g_iconLoaded;
}

AppIconData GetAppIconData_nid_postfix() {
    ensureIconLoaded();
    if (!g_iconLoaded) throw std::runtime_error("icon not available");
    return AppIconData{ g_iconBytes.data(), g_iconBytes.size() };
}

}
