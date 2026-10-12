cmake_minimum_required(VERSION 3.20)
cmake_policy(SET CMP0091 NEW)
project(MemoPnmMemoryTests LANGUAGES C)
if(NOT WIN32 OR NOT MSVC)
  message(FATAL_ERROR "These regressions require actual Windows MSVC")
endif()
set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded)
find_package(Leptonica 1.87.0 EXACT CONFIG REQUIRED)

function(memo_native_target name)
  add_executable(${name} memo_pnm_memory_tests.c)
  target_include_directories(${name} PRIVATE ${Leptonica_INCLUDE_DIRS})
  target_link_libraries(${name} PRIVATE ${Leptonica_LIBRARIES})
  target_compile_options(${name} PRIVATE /W4)
  target_compile_definitions(${name} PRIVATE _CRT_SECURE_NO_WARNINGS)
endfunction()

# Link the actual installed library. This is the original-library RED and
# rebuilt-library GREEN target; it never substitutes the decoder under test.
memo_native_target(memo_pnm_memory_tests)

# Compile the exact replacement text with test-local pixCreate counting. MSVC
# Leptonica intentionally ignores setPixMemoryManager, so it cannot prove this.
option(MEMO_PNM_BUILD_REPLACEMENT_TESTS "Build allocation-boundary checks" OFF)
if(MEMO_PNM_BUILD_REPLACEMENT_TESTS)
  memo_native_target(memo_pnm_memory_boundaries)
  target_compile_definitions(memo_pnm_memory_boundaries PRIVATE MEMO_PNM_REPLACEMENT_TEST)
endif()
