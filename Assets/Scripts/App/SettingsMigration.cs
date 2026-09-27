using System;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using UnityEngine;

namespace SCOdyssey.App
{
    public enum SettingsLoadOutcome
    {
        Empty,          // 저장된 설정 없음(기본값)
        Current,        // v2 그대로
        MigratedFromV1, // v1을 v2로 옮김(원문은 .bak에 한 번 남긴다)
        Corrupt         // 파싱 실패(원문은 .corrupt.bak에 남기고 기본값)
    }

    // 설정 JSON 읽기, v1 → v2 마이그레이션, 손상 값 검증. PlayerPrefs 읽기·쓰기는 SettingsManager가 한다(키는 v1 그대로 유지).
    //   v1 판별: JSON에 settingsVersion이 없다(JsonUtility는 없는 필드를 기본값으로 채우므로 필드 값으로는 알 수 없다).
    //   v1 → v2: 출력은 WASAPI, 장치는 기본 장치 따라가기(v1 인덱스는 믿을 수 없다), 버퍼 길이는 audioBufferIndex로 구한다.
    //   검증: 모르는 출력 타입은 WASAPI, 파싱할 수 없는 GUID는 빈 값, 프리셋에 없는 길이는 가까운 프리셋, 개수는 2~8 밖이면 타입 기본값.
    public static class SettingsMigration
    {
        // v1에만 있던 출력 필드(SettingsData에서는 지웠다). 마이그레이션 때 원문에서 따로 읽는다.
        [Serializable]
        private sealed class V1Fields
        {
            public int audioBufferIndex = 2;
        }

        public const string PrefsKey = "SCOdyssey.Settings.v1";
        public const string V1BackupKey = PrefsKey + ".bak";
        public const string CorruptBackupKey = PrefsKey + ".corrupt.bak";

        private const string VersionField = "\"settingsVersion\"";
        private const int MinBufferCount = 2;
        private const int MaxBufferCount = 8;

        public static SettingsData Parse(string json, out SettingsLoadOutcome outcome)
        {
            if (string.IsNullOrEmpty(json))
            {
                outcome = SettingsLoadOutcome.Empty;
                return new SettingsData();
            }

            SettingsData data = null;
            try
            {
                data = JsonAdapter.FromJson<SettingsData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Settings] 설정 파싱 실패, 기본값으로 시작합니다: " + e.Message);
            }
            if (data == null)
            {
                outcome = SettingsLoadOutcome.Corrupt;
                return new SettingsData();
            }

            if (json.IndexOf(VersionField, StringComparison.Ordinal) < 0)
            {
                MigrateFromV1(data, JsonAdapter.FromJson<V1Fields>(json).audioBufferIndex);
                outcome = SettingsLoadOutcome.MigratedFromV1;
            }
            else
            {
                outcome = SettingsLoadOutcome.Current;
            }
            Validate(data);
            return data;
        }

        public static void MigrateFromV1(SettingsData data, int v1BufferIndex)
        {
            data.settingsVersion = SettingsData.CurrentVersion;
            data.audioOutputType = AudioSettingsMapper.Wasapi;
            data.deviceGuid = "";
            data.deviceName = "";
            data.systemRate = 0;
            data.dspBufferLength = AudioSettingsMapper.BufferLengthForIndex(v1BufferIndex);
            data.dspBufferCount = AudioSettingsMapper.WasapiBufferCount;
        }

        public static void Validate(SettingsData data)
        {
            data.settingsVersion = SettingsData.CurrentVersion;

            AudioOutputKind kind = AudioSettingsMapper.ToOutputKind(data.audioOutputType);
            data.audioOutputType = AudioSettingsMapper.ToOutputType(kind);

            if (AudioSettingsMapper.ParseDeviceGuid(data.deviceGuid) == Guid.Empty)
            {
                data.deviceGuid = "";
                data.deviceName = "";
            }
            if (data.deviceName == null) data.deviceName = "";
            if (data.systemRate < 0) data.systemRate = 0;

            data.dspBufferLength = AudioSettingsMapper.NearestPreset(data.dspBufferLength);
            if (data.dspBufferCount < MinBufferCount || data.dspBufferCount > MaxBufferCount)
            {
                data.dspBufferCount = AudioSettingsMapper.DefaultBufferCount(kind);
            }

            data.masterVolume = Mathf.Clamp01(data.masterVolume);
            data.bgmVolume = Mathf.Clamp01(data.bgmVolume);
            data.hitSoundVolume = Mathf.Clamp01(data.hitSoundVolume);
            data.sfxVolume = Mathf.Clamp01(data.sfxVolume);
        }
    }
}
