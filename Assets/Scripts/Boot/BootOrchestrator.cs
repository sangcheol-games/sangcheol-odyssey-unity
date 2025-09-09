using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCOdyssey.Boot
{
    [DefaultExecutionOrder(ExecutionOrder.VeryEarly)]
    public sealed class BootOrchestrator : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour[] installers;

        public void SetInstallers(MonoBehaviour[] list) => installers = list;
        public void UseInstallers(params MonoBehaviour[] list) => installers = list;

        private void Awake()
        {
            if (FindObjectsByType<BootOrchestrator>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            RunBoot();
        }

        private void RunBoot()
        {
            var src = (installers != null && installers.Length > 0)
                ? installers
                : GetComponentsInChildren<MonoBehaviour>(true);

            var all = new List<IInstaller>(16);
            foreach (var mb in src)
            {
                if (!mb) continue;
                if (mb is IInstaller ins) all.Add(ins);
            }

            var ordered = OrderInstallers(all);
            foreach (var ins in ordered)
            {
                try { ins.Install(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static IEnumerable<IInstaller> OrderInstallers(IList<IInstaller> list)
        {
            var phases = Enum.GetValues(typeof(BootPhase)).Cast<BootPhase>().OrderBy(p => (int)p);
            var byId = list.ToDictionary(x => x.Id, x => x);

            var result = new List<IInstaller>(list.Count);
            foreach (var phase in phases)
            {
                var group = list.Where(x => x.Phase == phase).OrderByDescending(x => x.Priority).ToList();
                var added = new HashSet<string>();

                bool progressed;
                do
                {
                    progressed = false;
                    foreach (var ins in group)
                    {
                        if (added.Contains(ins.Id)) continue;
                        if (ins.Requires == null || ins.Requires.All(r => result.Any(z => z.Id == r) || added.Contains(r)))
                        {
                            result.Add(ins);
                            added.Add(ins.Id);
                            progressed = true;
                        }
                    }
                } while (progressed);

                foreach (var ins in group)
                {
                    if (!added.Contains(ins.Id))
                        result.Add(ins);
                }
            }
            return result;
        }
    }
}
