#include "shelf_fee.h"

int32_t shelf_overdue_fee(int32_t days_late, int32_t daily_rate, int32_t cap)
{
    if (days_late < 0 || daily_rate < 0 || cap < 0)
        return -1;

    int64_t raw_fee = (int64_t)days_late * daily_rate;
    return raw_fee > cap ? cap : (int32_t)raw_fee;
}
