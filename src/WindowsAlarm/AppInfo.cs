namespace WindowsAlarm;

// The author explicitly chose these details for the public About dialog.
internal static class AppInfo
{
    public const string Author = "JaehongLIM2";
    public const string RepositoryUrl = "https://github.com/JaehongLIM2/alarm_program";
    public const string ContactEmail = "nu0044@naver.com";
    public static Uri RepositoryUri { get; } = new(RepositoryUrl);
    public static Uri ContactUri { get; } = new("mailto:" + ContactEmail);
}
