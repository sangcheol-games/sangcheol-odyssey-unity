using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SCOdyssey.Rhythm.Tests
{
    public static class TrackAssert
    {
        public static void SortedByTimeThenLane(JudgeNote[] track)
        {
            for (int i = 1; i < track.Length; i++)
            {
                JudgeNote a = track[i - 1];
                JudgeNote b = track[i];
                bool ordered = a.Time < b.Time || (a.Time == b.Time && a.Lane <= b.Lane);
                if (!ordered) Assert.Fail($"track[{i - 1}] {a} comes after track[{i}] {b}");
            }
        }

        // NoteData.id가 트랙 인덱스의 순열이고, 그 자리의 JudgeNote가 같은 노트를 가리키는지
        public static void IdsMatchTrack(ChartData chart, JudgeNote[] track)
        {
            List<NoteData> notes = chart.GetFullChartList()
                .SelectMany(l => l.Notes)
                .Where(n => n.id >= 0)
                .ToList();

            Assert.That(notes.Select(n => n.id).OrderBy(id => id), Is.EqualTo(Enumerable.Range(0, track.Length)));

            bool bodyHasId = chart.GetFullChartList().SelectMany(l => l.Notes)
                .Any(n => n.noteType == SCOdyssey.Domain.Service.Constants.NoteType.Holding && n.id >= 0);
            Assert.That(bodyHasId, Is.False, "본체(3)는 트랙에 들어가지 않는다");

            foreach (NoteData note in notes)
            {
                JudgeNote judge = track[note.id];
                bool same = judge.Time == note.time
                    && judge.Lane == LaneMap.FromChartLine(note.laneIndex)
                    && NoteKinds.TryFrom(note.noteType, out NoteKind kind) && judge.Kind == kind;
                if (!same) Assert.Fail($"note id {note.id} ({note.time} line {note.laneIndex} {note.noteType}) != track {judge}");
            }
        }
    }
}
