import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from chartporter.chart_io import parse_raw, to_chart  # noqa: E402


def chart_from_text(text: str, path: str | None = None):
    """테스트용: CRLF 로 맞춘 텍스트 → (chart, raw)."""
    data = text.strip("\n").replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8") + b"\r\n"
    raw = parse_raw(data)
    return to_chart(raw, path), raw


HEADER = "#TITLE T\n#ARTIST A\n#DIFFICULTY Hard\n#LEVEL 1\n#BPM 120\n#NOTES {n}\n\n"
