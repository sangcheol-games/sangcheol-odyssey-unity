using UnityEngine;
using SCOdyssey.Testing.Config;

namespace SCOdyssey.Testing.Boot
{
    public sealed class TestBootEntryPoint : MonoBehaviour
    {
        [SerializeField] private TestingConfig config;
        public TestingConfig Config => config;
    }
}
