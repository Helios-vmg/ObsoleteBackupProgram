#pragma once
#include <windows.h>
#ifdef min
#undef min
#endif
#ifdef max
#undef max
#endif
#include <iostream>
#include <fstream>
#include <iomanip>
#include <algorithm>
#include <memory>
#include <string>
#include <exception>
#include <cryptlib/sha.h>
#include <cryptlib/files.h>
#include <cryptlib/base64.h>
#include <cryptlib/rsa.h>
#include <cryptlib/oaep.h>
#include <cryptlib/osrng.h>
#include <cryptlib/cryptlib.h>
#include <type_traits>
#include <vector>
#include <cstdint>
#include <sstream>
#include <deque>
#include <utility>
#include <cassert>
#include <vss.h>
#include <vswriter.h>
#include <vsbackup.h>
#include <limits>

#include "SimpleTypes.h"
#include "GlobalConstants.h"
