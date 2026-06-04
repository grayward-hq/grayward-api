
namespace Application.Helpers;

public static class DeviceNameParser
{
    public static string Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "Unknown device";

        var ua = userAgent;

        var browser = ua switch
        {
            _ when ua.Contains("Edg/")     => "Edge",
            _ when ua.Contains("OPR/")     => "Opera",
            _ when ua.Contains("Chrome/")  => "Chrome",
            _ when ua.Contains("Firefox/") => "Firefox",
            _ when ua.Contains("Safari/") && !ua.Contains("Chrome/") => "Safari",
            _ => "Browser"
        };

        var os = ua switch
        {
            _ when ua.Contains("iPhone")   => "iPhone",
            _ when ua.Contains("iPad")     => "iPad",
            _ when ua.Contains("Android")  => "Android",
            _ when ua.Contains("Windows")  => "Windows",
            _ when ua.Contains("Macintosh") => "macOS",
            _ when ua.Contains("Linux")    => "Linux",
            _ => "Unknown OS"
        };

        return $"{browser} on {os}";
    }
}