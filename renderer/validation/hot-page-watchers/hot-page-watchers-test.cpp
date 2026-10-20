#include <cstdio>
#include "video_core/buffer_cache/region_manager.h"
namespace Common { bool PerfFeatureEnabled(u32){return true;} WorkCounters& GetWorkCounters(){static WorkCounters c;return c;} }
int main(){
 using namespace VideoCore;
 for(u64 hot_page:{17ULL,130ULL,350ULL}){
  PageManager tracker;RegionManager region(&tracker,0x100000000ULL);
  for(int n=0;n<8;n++)region.NoteCpuWriteFault(hot_page*4096,4);
  region.UntrackHotPages(0,512*4096);
  for(u64 page=0;page<NUM_REGION_PAGES;page++)if(tracker.counts[page]!=(page==hot_page?1:0)){
   printf("FAIL hot=%llu unexpected watcher page=%llu count=%d\n",hot_page,page,tracker.counts[page]);return 1;
  }
  region.ChangeRegionState<StateOp::Set,StateOp::None>(0,512*4096);
  for(auto count:tracker.counts)if(count){puts("FAIL watcher remains after CPU invalidation");return 2;}
 }
 puts("PASS: mixed hot/cold words track only hot pages and CPU invalidation removes every watcher");
}
