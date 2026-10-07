using System;
using System.Collections.Generic;
using NUnit.Framework;
using SCOdyssey.Audio.Engine;

namespace SCOdyssey.Audio.Tests
{
    public class BootPlanTests
    {
        private static readonly Guid Device = new Guid("11111111-2222-3333-4444-555555555555");

        [Test]
        public void Requested_ThenWasapiDefault_ThenNoSound()
        {
            var requested = new AudioOutputRequest(AudioOutputKind.Wasapi, Device, "Speakers", 256, 4);

            List<BootAttempt> plan = BootPlan.Build(requested, false, true);

            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(Device, plan[0].DeviceId);
            Assert.AreEqual(256, plan[0].BufferLength);
            Assert.AreEqual(AudioOutputKind.Wasapi, plan[1].Kind);
            Assert.AreEqual(Guid.Empty, plan[1].DeviceId);
            Assert.AreEqual(512, plan[1].BufferLength);
            Assert.AreEqual(AudioOutputKind.NoSound, plan[2].Kind);
        }

        [Test]
        public void SafeMode_SkipsRequested()
        {
            var requested = new AudioOutputRequest(AudioOutputKind.Asio, Device, "ASIO4ALL", 256, 2);

            List<BootAttempt> plan = BootPlan.Build(requested, true, true);

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(AudioOutputKind.Wasapi, plan[0].Kind);
            Assert.AreEqual(AudioOutputKind.NoSound, plan[1].Kind);
        }

        [Test]
        public void AsioNotAllowed_SkipsAsioRequest()
        {
            var requested = new AudioOutputRequest(AudioOutputKind.Asio, Device, "ASIO4ALL", 256, 2);

            List<BootAttempt> plan = BootPlan.Build(requested, false, false);

            Assert.AreEqual(AudioOutputKind.Wasapi, plan[0].Kind);
            Assert.AreEqual(2, plan.Count);
        }

        [Test]
        public void Apply_Requested_ThenPrevious_ThenWasapiDefault_ThenNoSound()
        {
            var requested = new AudioOutputRequest(AudioOutputKind.Asio, Device, "ASIO4ALL", 256, 2);
            var previous = new BootAttempt(AudioOutputKind.Wasapi, Guid.Empty, "", 256, 4, "이전");

            List<BootAttempt> plan = BootPlan.BuildApply(requested, previous, true);

            Assert.AreEqual(4, plan.Count);
            Assert.AreEqual(AudioOutputKind.Asio, plan[0].Kind);
            Assert.AreEqual(AudioOutputKind.Wasapi, plan[1].Kind);
            Assert.AreEqual(256, plan[1].BufferLength);
            Assert.AreEqual(512, plan[2].BufferLength);
            Assert.AreEqual(AudioOutputKind.NoSound, plan[3].Kind);
        }

        [Test]
        public void Apply_WithoutPrevious_SkipsIt()
        {
            var requested = new AudioOutputRequest(AudioOutputKind.Wasapi, Device, "Speakers", 256, 4);

            List<BootAttempt> plan = BootPlan.BuildApply(requested, default, false);

            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(Device, plan[0].DeviceId);
        }

        [Test]
        public void Recovery_WithSameFirstAndPrevious_IsNotDuplicated()
        {
            var current = new BootAttempt(AudioOutputKind.Wasapi, Device, "Speakers", 256, 4, "현재");

            List<BootAttempt> plan = BootPlan.BuildRecovery(current, current);

            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(Device, plan[0].DeviceId);
            Assert.AreEqual(Guid.Empty, plan[1].DeviceId);
            Assert.AreEqual(AudioOutputKind.NoSound, plan[2].Kind);
        }

        [Test]
        public void RequestEqualToWasapiDefault_IsNotTriedTwice()
        {
            var requested = new AudioOutputRequest(AudioOutputKind.Wasapi, Guid.Empty, "", 512, 4);

            List<BootAttempt> plan = BootPlan.Build(requested, false, true);

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(AudioOutputKind.NoSound, plan[1].Kind);
        }
    }
}
