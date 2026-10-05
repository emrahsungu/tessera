// The other translation unit of multi_assembly_test: only the shared assembly's own headers (generated-shared/).
#include "Tessera.Tests.Shared.tessera.hpp"

std::int16_t badge_level(sharedmodels::Badge badge) { return badge.level(); }
