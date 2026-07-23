find_package(FFMPEG REQUIRED)

add_library(DesktopDlnaCast.Media.Native SHARED
  src/adts_stream_writer.cpp
  src/adts_stream_writer.h
  include/ddc_media.h
  src/bounded_packet_queue.cpp
  src/bounded_packet_queue.h
  src/ddc_api.cpp
  src/d3d11_video_processor.cpp
  src/d3d11_video_processor.h
  src/graphics_capture_source.cpp
  src/graphics_capture_source.h
  src/media_foundation_aac_encoder.cpp
  src/media_foundation_aac_encoder.h
  src/media_foundation_h264_encoder.cpp
  src/media_foundation_h264_encoder.h
  src/media_foundation_mp3_encoder.cpp
  src/media_foundation_mp3_encoder.h
  src/media_session.cpp
  src/media_session.h
  src/mpeg_ts_muxer.cpp
  src/mpeg_ts_muxer.h
  src/software_video_processor.cpp
  src/software_video_processor.h
  src/wasapi_loopback_capture.cpp
  src/wasapi_loopback_capture.h)

target_compile_features(DesktopDlnaCast.Media.Native PRIVATE cxx_std_20)
target_compile_definitions(DesktopDlnaCast.Media.Native PRIVATE
  DDC_MEDIA_NATIVE_EXPORTS
  NOMINMAX
  WIN32_LEAN_AND_MEAN
  WINVER=0x0A00
  _WIN32_WINNT=0x0A00)
target_compile_options(DesktopDlnaCast.Media.Native PRIVATE
  $<$<CXX_COMPILER_ID:MSVC>:/W4 /WX /permissive- /EHsc /utf-8>)
target_include_directories(DesktopDlnaCast.Media.Native
  PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_include_directories(DesktopDlnaCast.Media.Native SYSTEM PRIVATE
  ${FFMPEG_INCLUDE_DIRS})
target_link_directories(DesktopDlnaCast.Media.Native PRIVATE ${FFMPEG_LIBRARY_DIRS})
target_link_libraries(DesktopDlnaCast.Media.Native PRIVATE
  ${FFMPEG_LIBRARIES}
  d3d11
  dxgi
  mf
  mfplat
  mfreadwrite
  mfuuid
  ole32
  runtimeobject
  windowsapp)

set_target_properties(DesktopDlnaCast.Media.Native PROPERTIES
  OUTPUT_NAME "DesktopDlnaCast.Media.Native")

if(BUILD_TESTING)
  add_executable(DesktopDlnaCast.Media.Native.Tests
    src/adts_stream_writer.cpp
    src/bounded_packet_queue.cpp
    src/d3d11_video_processor.cpp
    src/media_foundation_aac_encoder.cpp
    src/media_foundation_h264_encoder.cpp
    src/media_foundation_mp3_encoder.cpp
    src/mpeg_ts_muxer.cpp
    src/software_video_processor.cpp
    tests/bounded_packet_queue_tests.cpp
    tests/d3d11_video_processor_tests.cpp
    tests/media_foundation_aac_encoder_tests.cpp
    tests/media_foundation_h264_encoder_tests.cpp
    tests/mpeg_ts_muxer_tests.cpp
    tests/software_video_processor_tests.cpp
    tests/native_api_tests.cpp)
  target_compile_features(DesktopDlnaCast.Media.Native.Tests PRIVATE cxx_std_20)
  target_compile_definitions(DesktopDlnaCast.Media.Native.Tests PRIVATE
    NOMINMAX
    WIN32_LEAN_AND_MEAN
    WINVER=0x0A00
    _WIN32_WINNT=0x0A00)
  target_include_directories(DesktopDlnaCast.Media.Native.Tests PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/src)
  target_include_directories(DesktopDlnaCast.Media.Native.Tests SYSTEM PRIVATE
    ${FFMPEG_INCLUDE_DIRS})
  target_link_directories(DesktopDlnaCast.Media.Native.Tests PRIVATE
    ${FFMPEG_LIBRARY_DIRS})
  target_compile_options(DesktopDlnaCast.Media.Native.Tests PRIVATE
    $<$<CXX_COMPILER_ID:MSVC>:/W4 /WX /permissive- /EHsc /utf-8>)
  target_link_libraries(DesktopDlnaCast.Media.Native.Tests PRIVATE
    DesktopDlnaCast.Media.Native
    ${FFMPEG_LIBRARIES}
    d3d11
    mf
    mfplat
    mfuuid
    runtimeobject)
  add_test(NAME DesktopDlnaCast.Media.Native.Components
    COMMAND DesktopDlnaCast.Media.Native.Tests)
endif()
