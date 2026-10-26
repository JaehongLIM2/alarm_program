using WindowsAlarm.Models;

namespace WindowsAlarm.Services;

public interface IWakeScheduler
{
    void Register(AlarmSchedule schedule);
    void Remove();
    bool Exists();
}

public interface IPowerService
{
    string GetDiagnostics();
    void EnsureHibernateAvailable();
    void Hibernate();
    void Shutdown();
    void KeepAwake(bool enabled);
}
