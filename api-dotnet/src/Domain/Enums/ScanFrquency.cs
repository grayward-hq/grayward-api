namespace Domain.Enums;

public enum ScanFrequency
{
    Hourly,
    Every6Hours,
    Every12Hours,
    Daily,
    Weekly
}

public static class ScanFrequencyExtensions
{
    public static TimeSpan ToTimeSpan(this ScanFrequency frequency) =>
        frequency switch
        {
            ScanFrequency.Hourly       => TimeSpan.FromHours(1),
            ScanFrequency.Every6Hours  => TimeSpan.FromHours(6),
            ScanFrequency.Every12Hours => TimeSpan.FromHours(12),
            ScanFrequency.Daily        => TimeSpan.FromHours(24),
            ScanFrequency.Weekly       => TimeSpan.FromDays(7),
            _                          => TimeSpan.FromHours(24)
        };
}