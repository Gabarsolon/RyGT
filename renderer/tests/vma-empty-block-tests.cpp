#include "video_core/renderer_vulkan/vk_common.h"
#include <vk_mem_alloc.h>
#include <cstdio>
#include <cstdlib>
#include <vector>
void check(bool ok,const char* why){if(!ok){std::fprintf(stderr,"FAIL: %s\n",why);std::exit(1);}}
int main(){
 vk::detail::DynamicLoader loader;
 auto gpa=loader.getProcAddress<PFN_vkGetInstanceProcAddr>("vkGetInstanceProcAddr");
 check(gpa!=nullptr,"Vulkan loader");VULKAN_HPP_DEFAULT_DISPATCHER.init(gpa);
 vk::ApplicationInfo app{.pApplicationName="RyGT allocator test",.apiVersion=VK_API_VERSION_1_1};
 auto made=vk::createInstance(vk::InstanceCreateInfo{.pApplicationInfo=&app});check(made.result==vk::Result::eSuccess,"instance");auto inst=made.value;
 VULKAN_HPP_DEFAULT_DISPATCHER.init(inst);
 auto devices=inst.enumeratePhysicalDevices();check(devices.result==vk::Result::eSuccess&&!devices.value.empty(),"physical device");auto physical=devices.value[0];
 for(auto p:devices.value)if(p.getProperties().deviceType==vk::PhysicalDeviceType::eDiscreteGpu){physical=p;break;}
 auto props=physical.getMemoryProperties();uint32_t mem=~0u;
 for(uint32_t i=0;i<props.memoryTypeCount;++i)if(props.memoryTypes[i].propertyFlags & vk::MemoryPropertyFlagBits::eDeviceLocal){mem=i;break;}
 check(mem!=~0u,"device-local type");
 auto queues=physical.getQueueFamilyProperties();uint32_t q=0;while(q<queues.size()&&!(queues[q].queueFlags&vk::QueueFlagBits::eGraphics))++q;check(q<queues.size(),"graphics queue");
 float priority=1;vk::DeviceQueueCreateInfo queue{.queueFamilyIndex=q,.queueCount=1,.pQueuePriorities=&priority};
 auto madeDevice=physical.createDevice(vk::DeviceCreateInfo{.queueCreateInfoCount=1,.pQueueCreateInfos=&queue});check(madeDevice.result==vk::Result::eSuccess,"device");auto device=madeDevice.value;
 VULKAN_HPP_DEFAULT_DISPATCHER.init(device);
 VmaVulkanFunctions funcs{};funcs.vkGetInstanceProcAddr=gpa;funcs.vkGetDeviceProcAddr=VULKAN_HPP_DEFAULT_DISPATCHER.vkGetDeviceProcAddr;
 VmaAllocatorCreateInfo ai{};ai.physicalDevice=physical;ai.device=device;ai.instance=inst;ai.vulkanApiVersion=VK_API_VERSION_1_1;ai.pVulkanFunctions=&funcs;ai.preferredLargeHeapBlockSize=16ull<<20;
 VmaAllocator allocator{};check(vmaCreateAllocator(&ai,&allocator)==VK_SUCCESS,"allocator");
 VkMemoryRequirements req{};req.size=1ull<<20;req.alignment=256;req.memoryTypeBits=1u<<mem;
 VmaAllocationCreateInfo ac{};ac.usage=VMA_MEMORY_USAGE_GPU_ONLY;ac.requiredFlags=VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
 std::vector<VmaAllocation> allocations(8);
 for(auto& a:allocations)check(vmaAllocateMemory(allocator,&req,&ac,&a,nullptr)==VK_SUCCESS,"allocate");
 VmaAllocationInfo original{};vmaGetAllocationInfo(allocator,allocations[0],&original);
 for(size_t i=1;i<allocations.size();++i)vmaFreeMemory(allocator,allocations[i]);
 auto firstFreed=Vulkan::TrimEmptyVmaBlocks(allocator);
 VmaAllocationInfo live{};vmaGetAllocationInfo(allocator,allocations[0],&live);
 check(live.deviceMemory==original.deviceMemory&&live.offset==original.offset&&live.size==original.size,"live allocation survives trim unchanged");
 VmaTotalStatistics stats{};vmaCalculateStatistics(allocator,&stats);check(stats.total.statistics.allocationCount==1&&stats.total.statistics.blockBytes>=req.size,"occupied block retained");
 vmaFreeMemory(allocator,allocations[0]);auto finalFreed=Vulkan::TrimEmptyVmaBlocks(allocator);
 vmaCalculateStatistics(allocator,&stats);check(stats.total.statistics.allocationCount==0&&stats.total.statistics.blockCount==0,"all empty default blocks returned");
 std::printf("PASS: occupied block retained, live allocation unchanged, empty blocks reclaimed (%llu + %llu bytes)\n",static_cast<unsigned long long>(firstFreed),static_cast<unsigned long long>(finalFreed));
 vmaDestroyAllocator(allocator);device.destroy();inst.destroy();
}
