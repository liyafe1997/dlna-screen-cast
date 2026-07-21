cmake_minimum_required(VERSION 4.2)

project(DesktopDlnaCastNative LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "DesktopDlnaCast.Media.Native supports Windows only")
endif()

include(CTest)
add_subdirectory(src/DesktopDlnaCast.Media.Native)
