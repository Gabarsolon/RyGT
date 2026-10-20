#include "common/unmapped_allocations.h"
#include "video_core/buffer_cache/range_set.h"
#include <array>
#include <cstdio>
#include <random>
#include <vector>
struct Backing { u64 start,end; unsigned memory; };
int main(){
 constexpr u64 base=0x2900000000ULL, page=16384;
 std::vector<Backing> ranges{{base,base+4*page,1},{base+8*page,base+12*page,1},{base+16*page,base+20*page,2}};
 VideoCore::RangeSet retired;
 auto select=[&]{return Common::FullyUnmappedAllocations(ranges,[&](const Backing& r){return retired.Contains(r.start,r.end-r.start);});};
 for(unsigned i=0;i<4;++i) retired.Add(base+i*page,page);
 if(select()[1])return 1; // Shared allocation still has its second disjoint range live.
 for(unsigned i=8;i<12;++i) retired.Add(base+i*page,page);
 if(!select()[1]||select()[2])return 2;
 retired.Subtract(base+2*page,page); // Remapping a subpage cancels retirement.
 if(select()[1])return 3;
 retired.Add(base+2*page,page);
 if(!select()[1])return 4;
 retired.Clear(); retired.Add(base,4*page-1); retired.Add(base+8*page,4*page);
 if(select()[1])return 6;
 retired.Add(base+4*page-1,1); if(!select()[1])return 7;
 retired.Subtract(base+1,1); if(select()[1])return 8;
 std::array<bool,32> oracle{}; retired.Clear();std::mt19937 rng(5);
 for(unsigned n=0;n<5000;++n){
  unsigned i=rng()%32;bool unmap=rng()%2;
  oracle[i]=unmap;
  if(unmap)retired.Add(base+i*page,page);else retired.Subtract(base+i*page,page);
  auto result=select();
  for(unsigned id:{1u,2u}){
   bool expected=true;
   for(const auto& r:ranges)if(r.memory==id)for(u64 at=r.start;at<r.end;at+=page)expected &= oracle[(at-base)/page];
   if(result[id]!=expected)return 5;
  }
 }
 puts("PASS: cumulative subpage unmaps, shared/disjoint backing, remap cancellation, 5000 randomized lifetime transitions");
}
