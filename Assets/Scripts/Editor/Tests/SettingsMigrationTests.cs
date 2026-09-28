using System;
using NUnit.Framework;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;

namespace SCOdyssey.App.Tests
{
    public class SettingsMigrationTests
    {
        private const string SomeGuid = "0b0a2c64-6c41-4d0b-9c2c-6a3a0e2f8a11";

        [Test]
        public void Empty_ReturnsDefaults()
        {
            SettingsData data = SettingsMigration.Parse("", out SettingsLoadOutcome outcome);

            Assert.AreEqual(SettingsLoadOutcome.Empty, outcome);
            Assert.AreEqual(SettingsData.CurrentVersion, data.settingsVersion);
            Assert.AreEqual("WASAPI", data.audioOutputType);
            Assert.AreEqual(256, data.dspBufferLength);
            Assert.AreEqual(4, data.dspBufferCount);
        }

        [Test]
        public void Corrupt_ReturnsDefaults()
        {
            SettingsData data = SettingsMigration.Parse("{not json", out SettingsLoadOutcome outcome);

            Assert.AreEqual(SettingsLoadOutcome.Corrupt, outcome);
            Assert.AreEqual(256, data.dspBufferLength);
        }

        [TestCase(0, 64)]
        [TestCase(1, 128)]
        [TestCase(2, 256)]
        [TestCase(3, 512)]
        [TestCase(4, 1024)]
        [TestCase(9, 256)]
        [TestCase(-1, 256)]
        public void V1_BufferIndexBecomesLength(int index, int expectedLength)
        {
            string json = "{\"audioBufferIndex\":" + index + ",\"audioDeviceIndex\":3}";

            SettingsData data = SettingsMigration.Parse(json, out SettingsLoadOutcome outcome);

            Assert.AreEqual(SettingsLoadOutcome.MigratedFromV1, outcome);
            Assert.AreEqual(expectedLength, data.dspBufferLength);
            Assert.AreEqual(4, data.dspBufferCount);
            Assert.AreEqual("WASAPI", data.audioOutputType);
            Assert.AreEqual("", data.deviceGuid);
            Assert.AreEqual(SettingsData.CurrentVersion, data.settingsVersion);
        }

        [Test]
        public void V1_KeepsOtherSettings()
        {
            string json = "{\"audioOffsetMs\":12,\"judgmentOffset\":-3,\"masterVolume\":0.5,\"bgmVolume\":0.25,\"playInBackground\":true,\"targetFrameRate\":120,\"audioBufferIndex\":1}";

            SettingsData data = SettingsMigration.Parse(json, out _);

            Assert.AreEqual(12, data.audioOffsetMs);
            Assert.AreEqual(-3, data.judgmentOffset);
            Assert.AreEqual(0.5f, data.masterVolume, 1e-6f);
            Assert.AreEqual(0.25f, data.bgmVolume, 1e-6f);
            Assert.IsTrue(data.playInBackground);
            Assert.AreEqual(120, data.targetFrameRate);
            Assert.AreEqual(128, data.dspBufferLength);
        }

        [Test]
        public void V2_RoundTripsOutputFields()
        {
            var saved = new SettingsData();
            saved.audioOutputType = "ASIO";
            saved.deviceGuid = SomeGuid;
            saved.deviceName = "Speakers";
            saved.systemRate = 44100;
            saved.dspBufferLength = 128;
            saved.dspBufferCount = 2;

            SettingsData data = SettingsMigration.Parse(JsonAdapter.ToJson(saved), out SettingsLoadOutcome outcome);

            Assert.AreEqual(SettingsLoadOutcome.Current, outcome);
            Assert.AreEqual("ASIO", data.audioOutputType);
            Assert.AreEqual(SomeGuid, data.deviceGuid);
            Assert.AreEqual("Speakers", data.deviceName);
            Assert.AreEqual(44100, data.systemRate);
            Assert.AreEqual(128, data.dspBufferLength);
            Assert.AreEqual(2, data.dspBufferCount);
        }

        [Test]
        public void Validate_FixesDamagedValues()
        {
            var data = new SettingsData();
            data.audioOutputType = "DirectSound";
            data.deviceGuid = "not-a-guid";
            data.deviceName = "Old";
            data.systemRate = -1;
            data.dspBufferLength = 300;
            data.dspBufferCount = 0;
            data.masterVolume = 2f;
            data.sfxVolume = -1f;

            SettingsMigration.Validate(data);

            Assert.AreEqual("WASAPI", data.audioOutputType);
            Assert.AreEqual("", data.deviceGuid);
            Assert.AreEqual("", data.deviceName);
            Assert.AreEqual(0, data.systemRate);
            Assert.AreEqual(256, data.dspBufferLength);
            Assert.AreEqual(4, data.dspBufferCount);
            Assert.AreEqual(1f, data.masterVolume);
            Assert.AreEqual(0f, data.sfxVolume);
        }

        [Test]
        public void Validate_AsioKeepsLengthAndUsesTwoBuffers()
        {
            var data = new SettingsData();
            data.audioOutputType = "asio";
            data.dspBufferCount = 99;
            data.dspBufferLength = 128;

            SettingsMigration.Validate(data);

            Assert.AreEqual("ASIO", data.audioOutputType);
            Assert.AreEqual(2, data.dspBufferCount);
            Assert.AreEqual(128, data.dspBufferLength);
        }

        [TestCase(1000, 1024)]
        [TestCase(480, 512)]
        [TestCase(384, 256)]
        [TestCase(100, 128)]
        [TestCase(1, 64)]
        [TestCase(5000, 1024)]
        [TestCase(512, 512)]
        public void NearestPreset(int length, int expected)
        {
            Assert.AreEqual(expected, AudioSettingsMapper.NearestPreset(length));
        }

        [Test]
        public void BootRequest_UsesV2Fields()
        {
            var data = new SettingsData();
            data.audioOutputType = "ASIO";
            data.deviceGuid = SomeGuid;
            data.deviceName = "Speakers";
            data.dspBufferLength = 512;
            data.dspBufferCount = 2;

            AudioOutputRequest request = AudioSettingsMapper.ToBootRequest(data);

            Assert.AreEqual(AudioOutputKind.Asio, request.Kind);
            Assert.AreEqual(new Guid(SomeGuid), request.DeviceId);
            Assert.AreEqual("Speakers", request.DeviceName);
            Assert.AreEqual(512, request.BufferLength);
            Assert.AreEqual(2, request.BufferCount);
        }

        [Test]
        public void BootRequest_EmptyGuidFollowsDefault()
        {
            var data = new SettingsData();
            data.deviceName = "Stale name";

            AudioOutputRequest request = AudioSettingsMapper.ToBootRequest(data);

            Assert.AreEqual(AudioOutputKind.Wasapi, request.Kind);
            Assert.AreEqual(Guid.Empty, request.DeviceId);
            Assert.AreEqual("", request.DeviceName);
            Assert.AreEqual(256, request.BufferLength);
            Assert.AreEqual(4, request.BufferCount);
        }

        [Test]
        public void JudgmentOffsetSteps_ComeFromSettings()
        {
            var data = new SettingsData();
            data.judgmentOffset = -7;

            Assert.AreEqual(-7, AudioSettingsMapper.ToJudgmentOffsetSteps(data));
            Assert.AreEqual(0, AudioSettingsMapper.ToJudgmentOffsetSteps(null));
        }
    }
}
