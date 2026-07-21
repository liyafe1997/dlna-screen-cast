#pragma once

#include <cstdlib>
#include <iostream>

namespace ddc::test
{
    [[noreturn]] inline void fail(const char* expression, const char* file, int line)
    {
        std::cerr << file << ':' << line << ": check failed: " << expression << '\n';
        std::abort();
    }
}

#define DDC_TEST_CHECK(expression) \
    do \
    { \
        if (!(expression)) \
        { \
            ::ddc::test::fail(#expression, __FILE__, __LINE__); \
        } \
    } while (false)
