using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using WindowsAlarm.Models;

namespace WindowsAlarm.Services;

public sealed class TaskSchedulerWakeService : IWakeScheduler
{
    private readonly string _exe;
    private readonly string _dataRoot;
    public string UserSid { get; } = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("사용자 SID 없음");
    public string TaskName { get; }

    public TaskSchedulerWakeService(string dataRoot, string? executable = null)
    {
        _dataRoot = Path.GetFullPath(dataRoot);
        _exe = executable ?? AppExecutable.GetPath();
        TaskName = $"WindowsAlarm-{UserSid}-{ProfileKey(_dataRoot)}";
    }

    public static string ProfileKey(string root) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant())))[..16];

    public string BuildXml(AlarmSchedule schedule)
    {
        if (!Path.IsPathFullyQualified(_exe) || !File.Exists(_exe)) throw new FileNotFoundException("실행 파일을 찾을 수 없습니다. 빌드/배포 폴더에서 실행하세요.", _exe);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name, object? content) => new(ns + name, content);
        var time = schedule.DueAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        // Task Scheduler persists across app exit/reboot. A waitable-timer handle alone cannot do this.
        // InteractiveToken gives audible WPF playback in the user's existing (also locked) session, without a password.
        return new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.3"),
            E("RegistrationInfo", E("Description", "Windows Alarm: S4 wake and interactive alarm recovery")),
            E("Triggers", new object[] {
                E("TimeTrigger", new object[] {
                    E("Repetition", new object[] { E("Interval", "PT1M"), E("Duration", "PT2H"), E("StopAtDurationEnd", false) }),
                    E("StartBoundary", time), E("EndBoundary", schedule.DueAt.Add(AlarmTiming.RecoveryGrace).AddSeconds(1).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)), E("Enabled", true) }),
                E("LogonTrigger", new object[] { E("StartBoundary", time), E("Enabled", true), E("UserId", UserSid) }) }),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "CurrentUser"),
                E("UserId", UserSid), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
            E("Settings", new object[] {
                E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", false), E("StopIfGoingOnBatteries", false),
                E("AllowHardTerminate", false), E("StartWhenAvailable", true), E("RunOnlyIfNetworkAvailable", false),
                E("AllowStartOnDemand", true), E("Enabled", true), E("WakeToRun", true), E("ExecutionTimeLimit", "PT0S"),
                E("RestartOnFailure", new object[] { E("Interval", "PT1M"), E("Count", 3) }) }),
            new XElement(ns + "Actions", new XAttribute("Context", "CurrentUser"), E("Exec", new object[] {
                E("Command", _exe), E("Arguments", $"--wake {schedule.Id:D} --data-dir {QuoteArgument(_dataRoot)}"),
                E("WorkingDirectory", Path.GetDirectoryName(_exe)) })))).ToString();
    }

    public static string QuoteArgument(string value)
    {
        // Windows command-line quoting, including trailing backslashes. No shell is involved.
        var builder = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            builder.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            builder.Append(c); slashes = 0;
        }
        return builder.Append('\\', slashes * 2).Append('"').ToString();
    }

    public void Register(AlarmSchedule schedule)
    {
        var xml = BuildXml(schedule);
        WithFolder(folder => { object registered = folder.RegisterTask(TaskName, xml, 6, UserSid, null, 3, null); Release(registered); return true; });
        // A successful registration must be readable before the caller is allowed to hibernate.
        if (!Exists()) throw new InvalidOperationException("Wake Timer 등록 확인에 실패했습니다.");
    }

    public bool Exists() => WithFolder(folder =>
    {
        try { object task = folder.GetTask(TaskName); Release(task); return true; }
        catch (Exception ex) when (IsMissingTask(ex)) { return false; }
    });

    public string? ReadRegisteredXml() => WithFolder<string?>(folder =>
    {
        try { dynamic task = folder.GetTask(TaskName); try { return (string)task.Xml; } finally { Release(task); } }
        catch (Exception ex) when (IsMissingTask(ex)) { return null; }
    });

    public void Remove() => WithFolder(folder =>
    {
        try { folder.DeleteTask(TaskName, 0); }
        catch (Exception ex) when (IsMissingTask(ex)) { /* already absent */ }
        return true;
    });

    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("작업 스케줄러가 없습니다."))!;
        object? folder = null;
        try { service.Connect(); folder = service.GetFolder("\\"); return action(folder); }
        finally { Release(folder); Release(service); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    private static bool IsMissingTask(Exception error) => error is FileNotFoundException or DirectoryNotFoundException ||
        (uint)error.HResult is 0x80070002 or 0x80070003;
}
