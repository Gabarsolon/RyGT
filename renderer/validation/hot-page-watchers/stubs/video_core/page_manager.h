#pragma once
#include "video_core/buffer_cache/region_definitions.h"
namespace VideoCore {
enum class PageOp : s8 { None=0, Track=1, Untrack=-1 };
class PageManager {
public:
 std::array<int,NUM_REGION_PAGES> counts{};
 void UpdatePageWatchersForRegion(VAddr,const Bounds& b,const RegionBits& w,const RegionBits& r,PageOp wo,PageOp ro){
  for(u64 page=b.start_word*64+b.start_page;page<=b.end_word*64+b.end_page;page++){
   if(wo!=PageOp::None&&w.GetPage(page))counts[page]+=static_cast<int>(wo);
   if(ro!=PageOp::None&&r.GetPage(page))counts[page]+=static_cast<int>(ro);
  }
 }
};
}
