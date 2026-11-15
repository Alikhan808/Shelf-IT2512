#ifndef SHELF_FEE_H
#define SHELF_FEE_H

#include <stdint.h>

#if defined(_WIN32)
#define SHELF_EXPORT __declspec(dllexport)
#else
#define SHELF_EXPORT __attribute__((visibility("default")))
#endif

/* Returns the capped fee in KZT, or -1 for a negative argument. */
SHELF_EXPORT int32_t shelf_overdue_fee(int32_t days_late, int32_t daily_rate, int32_t cap);

#endif
