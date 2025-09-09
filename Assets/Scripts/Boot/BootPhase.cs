namespace SCOdyssey.Boot
{
    public enum BootPhase
    {
        Core = 0,
        Telemetry = 10,
        Storage = 20,
        Network = 30,
        Auth = 40,
        Content = 50,
        UI = 100
    }
}