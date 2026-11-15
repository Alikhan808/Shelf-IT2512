using System.Runtime.InteropServices;
using Shelf.Core;

namespace Shelf.Boundary;

public static class NativeFee
{
    public const int DailyRate = 100;
    public const int FeeCap = 3000;

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [DllImport("shelf_fee", EntryPoint = "shelf_overdue_fee", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int Compute(int daysLate, int dailyRate, int cap);

    public static Result<int> Quote(int daysLate, int dailyRate, int cap)
    {
        try
        {
            int fee = Compute(daysLate, dailyRate, cap);
            return fee >= 0 && fee <= cap
                ? new Result<int>.Ok(fee)
                : new Result<int>.Fail("native-fee-rejected", "The native fee library rejected the quote.");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException)
        {
            return new Result<int>.Fail("native-unavailable", $"Could not load the Windows x64 fee library ({exception.GetType().Name}).");
        }
    }

    public static Result<Change> Return(Library state, string titleId, string memberId, DateOnly date) =>
        state.PrepareReturn(titleId, memberId, date).Bind(plan =>
            Quote(plan.DaysLate, DailyRate, FeeCap).Map(fee =>
                new Change(plan.State, $"Returned {titleId} from {memberId}. {plan.DaysLate} day(s) late. Fee {fee} KZT.")));
}
