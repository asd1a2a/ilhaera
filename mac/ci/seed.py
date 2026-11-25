"""CI 스크린샷용 예시 데이터 (Mac·Windows 공용).
사용: python3 seed.py light|dark|glass|done [설정=값 ...]   예: seed.py glass glassTint=60"""
import json, os, sys, datetime as dt

theme = sys.argv[1] if len(sys.argv) > 1 else "light"
now = dt.datetime.now().replace(second=0, microsecond=0)
f = lambda d: d.strftime("%Y-%m-%dT%H:%M:%S")
at = lambda days, h, m: (now + dt.timedelta(days=days)).replace(hour=h, minute=m)

items = [
    dict(id="a", text="팀 주간 회의 자료 준비", due=f(now + dt.timedelta(minutes=22)), notified=False),
    dict(id="b", text="병원 예약 전화하기", due=f(now - dt.timedelta(minutes=40)), notified=True),
    dict(id="r1", text="영양제 먹기", due=f(now + dt.timedelta(hours=4, minutes=26)), notified=False, repeat="daily"),
    dict(id="c", text="3분기 매출 보고서 제출", due=f(at(1, 10, 0)), notified=False),
    dict(id="r2", text="스탠드업 미팅", due=f(at(3, 9, 30)), notified=False, repeat="weekdays"),
    dict(id="d", text="장보기: 우유, 계란, 사과", notified=False),
    dict(id="e", text="헬스장 등록", done=True, notified=True, completed=f(now)),
]
for i in items:
    i.setdefault("done", False)
    i["created"] = f(now)

# "done": 완료 줄(완료한 일 보기 / 모두 지우기)이 보이도록 목록을 줄이고 완료 항목을 펼친다
show_done = theme == "done"
if show_done:
    theme = "light"
    items = [i for i in items if i["id"] in ("a", "d", "e")] + [
        dict(id="e2", text="회의록 정리", done=True, notified=True, completed=f(now), created=f(now))]
settings = {"theme": theme, "reminderMinutes": 30, "autoStartInitialized": True, "opacity": 100, "showDone": show_done}
if theme == "glass":
    # 색 바꾸기도 함께 확인: 보라 강조 + 하늘색 형광펜, 꽤 투명한 유리
    settings.update(accentColor="#7B4FD6", markerColor="#A9DDFF", glassTint=20)

# 뒤에 붙인 "이름=값" 은 설정을 덮어쓴다
for pair in sys.argv[2:]:
    key, _, value = pair.partition("=")
    settings[key] = (value == "true") if value in ("true", "false") else int(value) if value.lstrip("-").isdigit() else value

if sys.platform == "win32":
    folder = os.path.join(os.environ["APPDATA"], "TodoWidget")
else:
    folder = os.path.expanduser("~/Library/Application Support/Ilhaera")
os.makedirs(folder, exist_ok=True)
with open(os.path.join(folder, "todos.json"), "w", encoding="utf-8") as fp:
    json.dump({"version": 1, "items": items,
               "settings": settings},
              fp, ensure_ascii=False)
