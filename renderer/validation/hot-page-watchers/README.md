Regression test for patch 0038. Compile the real region_manager.h with this directory's stubs first on the include path, followed by shadGT/src. Stubs supply settings and count watcher operations; the production hot-page transition and watcher-bound calculation run unchanged.

Use clang-cl /DNDEBUG /MD /std:c++latest /EHsc /O1 /clang:-ftrivial-auto-var-init=pattern with those include paths and hot-page-watchers-test.cpp. Pattern initialization makes the old uninitialized-mask error deterministic. Test hot pages in the first, middle, and last words of a mixed hot/cold range; require only those pages to acquire watchers and all counts to return to zero on CPU invalidation.

Locally, the original header fails at cold page 65 with an extra watcher; patch 0038 passes all cases. No GPU is required.
