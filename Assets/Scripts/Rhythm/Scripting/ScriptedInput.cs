using System.Globalization;

namespace SCOdyssey.Rhythm
{
    // 스크립트/오토플레이 입력 1건. 시각은 판정 엔진과 같은 게임 상대시간(초)
    public readonly struct ScriptedInput
    {
        public readonly double Time;
        public readonly Lane Lane;
        public readonly bool IsPress;

        public ScriptedInput(double time, Lane lane, bool isPress)
        {
            Time = time;
            Lane = lane;
            IsPress = isPress;
        }

        public static ScriptedInput Press(double time, Lane lane) => new(time, lane, true);
        public static ScriptedInput Release(double time, Lane lane) => new(time, lane, false);

        // InputScript 텍스트 한 줄 형식: "시각 레인(1~4) P|R"
        public override string ToString()
            => $"{Time.ToString("0.######", CultureInfo.InvariantCulture)} {(int)Lane + LaneMap.FIRST_LANE} {(IsPress ? "P" : "R")}";
    }
}
