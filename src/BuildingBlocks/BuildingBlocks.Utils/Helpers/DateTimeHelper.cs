namespace BuildingBlocks.Utils.Helpers;

public class DateTimeHelper
{
    public static DateTime GetDateTimeNow()
    {
        return DateTimeOffset.Now.UtcDateTime;
    }
}