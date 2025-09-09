using System.Collections.Generic;

namespace SCOdyssey.Boot
{
    public interface IInstaller
    {
        string Id { get; }
        IEnumerable<string> Requires { get; }
        BootPhase Phase { get; }
        int Priority { get; }
        void Install();
    }
}
