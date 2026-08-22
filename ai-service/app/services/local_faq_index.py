from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from threading import RLock


@dataclass(frozen=True)
class FaqBlueprint:
    """一类高频问题及其在手册中可验证的证据特征。"""

    id: str
    title: str
    questions: tuple[str, ...]
    question_terms: tuple[str, ...]
    source_terms: tuple[str, ...]
    preferred_chapters: tuple[str, ...]
    max_source_pages: int = 2


class LocalFaqIndex:
    """由已解析手册生成的本地高频问答索引。

    这不是用外部数据训练通用模型，而是把高频问法与本车型手册中实际
    命中的页码建立本地映射。重新解析或替换手册时会重建索引，因此答案
    始终回到当前车型的原文和页码。
    """

    _BLUEPRINTS = (
        FaqBlueprint(
            id="maintenance-overview",
            title="日常保养与保养周期",
            questions=("保养怎么做", "保养应该怎么做", "怎么保养", "如何保养", "多久保养一次"),
            question_terms=("保养", "维护", "照顾"),
            source_terms=("保养须知", "定期保养", "保养计划"),
            preferred_chapters=("6-2. 保养",),
        ),
        FaqBlueprint(
            id="engine-oil",
            title="检查或加注发动机机油",
            questions=("机油怎么检查", "机油怎么加", "机油保养怎么做", "机油多久换"),
            question_terms=("机油", "发动机油"),
            source_terms=("检查发动机机油", "加注发动机机油", "发动机机油"),
            preferred_chapters=("6-3. 自行保养",),
        ),
        FaqBlueprint(
            id="tire-pressure",
            title="轮胎与胎压检查",
            questions=("胎压怎么检查", "轮胎怎么保养", "轮胎气压多少", "轮胎没气怎么办"),
            question_terms=("胎压", "轮胎", "爆胎", "漏气", "扎胎"),
            source_terms=("轮胎气压", "检查轮胎", "轮胎压力"),
            preferred_chapters=("6-3. 自行保养",),
        ),
        FaqBlueprint(
            id="battery",
            title="12V 蓄电池检查",
            questions=("蓄电池怎么保养", "电瓶怎么检查", "12v电池怎么保养"),
            question_terms=("蓄电池", "电瓶", "12v电池"),
            source_terms=("12 V（伏）蓄电池", "12 V蓄电池", "蓄电池"),
            preferred_chapters=("6-3. 自行保养",),
        ),
        FaqBlueprint(
            id="air-conditioning",
            title="空调使用与保养",
            questions=("空调怎么用", "空调不制冷怎么办", "空调滤芯怎么换"),
            question_terms=("空调", "冷气", "不制冷", "空调滤芯"),
            source_terms=("自动空调系统", "空调滤清器", "空调"),
            preferred_chapters=("5-1. 使用空调系统",),
        ),
        FaqBlueprint(
            id="warning-lights",
            title="警告灯与警告信息",
            questions=("警告灯亮了怎么办", "故障灯亮了怎么办", "黄三角是什么意思"),
            question_terms=("警告灯", "故障灯", "黄三角", "警告信息"),
            source_terms=("警告灯 详细说明", "警告信息"),
            preferred_chapters=("7-2. 紧急情况下",),
        ),
        FaqBlueprint(
            id="start-failure",
            title="车辆无法起动",
            questions=("车打不着怎么办", "无法启动怎么办", "不能启动怎么办"),
            question_terms=("打不着", "无法启动", "不能启动", "起动不了"),
            source_terms=("混合动力系统不能起动", "不能起动", "无法起动"),
            preferred_chapters=("7-2. 紧急情况下",),
        ),
        FaqBlueprint(
            id="refueling",
            title="加油与燃油箱盖",
            questions=("怎么加油", "油箱盖怎么开", "加不了油怎么办"),
            question_terms=("加油", "油箱盖", "加不了油"),
            source_terms=("加油方法", "燃油加注口盖", "燃油箱"),
            preferred_chapters=("4-4. 加注燃油",),
        ),
        FaqBlueprint(
            id="driver-assistance-definition",
            title="主动驾驶辅助（PDA）",
            questions=("pda是什么", "主动驾驶辅助是什么", "pda有什么作用"),
            question_terms=("pda", "主动驾驶辅助"),
            source_terms=("PDA（主动驾驶辅助）", "主动驾驶辅助"),
            preferred_chapters=("4-5. 使用驾驶辅助系统",),
        ),
        FaqBlueprint(
            id="driver-assistance-settings",
            title="主动驾驶辅助（PDA）设定",
            questions=("pda怎么开启", "pda怎么关闭", "主动驾驶辅助怎么开启", "主动驾驶辅助怎么关闭"),
            question_terms=("pda怎么开启", "pda怎么关闭", "主动驾驶辅助怎么开启", "主动驾驶辅助怎么关闭"),
            source_terms=("更改主动驾驶辅助设定", "启用/ 禁用", "启用/禁用"),
            preferred_chapters=("4-5. 使用驾驶辅助系统",),
        ),
        FaqBlueprint(
            id="rear-seat-folding",
            title="后排座椅靠背折叠",
            questions=("后排座椅怎么放倒", "后排座椅怎么折叠", "后座怎么放倒"),
            question_terms=("后排座椅", "后座", "座椅放倒"),
            source_terms=("折叠后排座椅靠背", "后排座椅靠背可折叠"),
            preferred_chapters=("3-3. 调节座椅",),
        ),
        FaqBlueprint(
            id="key-battery",
            title="电子钥匙电池更换",
            questions=("钥匙电池怎么换", "电子钥匙没电怎么办", "遥控钥匙电池怎么换"),
            question_terms=("钥匙电池", "电子钥匙没电", "遥控钥匙电池"),
            source_terms=("电子钥匙电池", "使用锂电池CR2450", "插入新电池"),
            preferred_chapters=("6-3. 自行保养",),
        ),
        FaqBlueprint(
            id="fuse-replacement",
            title="保险丝检查与更换",
            questions=("保险丝怎么换", "保险丝烧了怎么办", "保险丝盒在哪里"),
            question_terms=("保险丝", "保险丝盒"),
            source_terms=("检查和更换保险丝", "保险丝盒", "保险丝是否熔断"),
            preferred_chapters=("6-3. 自行保养",),
        ),
        FaqBlueprint(
            id="bulb-replacement",
            title="灯泡更换",
            questions=("灯泡怎么换", "车灯坏了怎么办", "倒车灯怎么换"),
            question_terms=("灯泡", "车灯", "倒车灯"),
            source_terms=("更换灯泡", "需要由丰田汽车经销商更换的灯泡", "倒车灯（灯泡型）"),
            preferred_chapters=("6-3. 自行保养",),
        ),
        FaqBlueprint(
            id="windshield-wipers",
            title="雨刮器与喷洗器",
            questions=("雨刮器怎么用", "雨刷怎么用", "玻璃水怎么喷", "雨刮器不工作怎么办"),
            question_terms=("雨刮器", "雨刷", "刮水器", "喷洗器", "玻璃水"),
            source_terms=("风挡玻璃刮水器和喷洗器", "操作刮水器控制杆", "喷洗器/ 刮水器"),
            preferred_chapters=("4-3. 操作车灯和刮水器",),
        ),
        FaqBlueprint(
            id="hybrid-battery-vent",
            title="混合动力蓄电池进气通风口清洁",
            questions=("混动电池通风口怎么清洁", "牵引电池进气口怎么清洁", "电池冷却要保养吗"),
            question_terms=("电池通风口", "牵引电池", "进气通风口", "电池冷却"),
            source_terms=("清洁进气通风口", "进气通风口滤清器", "牵引用蓄电池冷却零部件"),
            preferred_chapters=("6-3. 自行保养",),
        ),
        FaqBlueprint(
            id="winter-driving",
            title="冬季驾驶要领",
            questions=("冬天开车要注意什么", "冬季驾驶注意事项", "下雪天怎么开车"),
            question_terms=("冬天", "冬季", "下雪天", "冰雪路面"),
            source_terms=("冬季驾驶要领", "冻结的车窗", "冰或雪"),
            preferred_chapters=("4-6. 驾驶要领",),
        ),
        FaqBlueprint(
            id="trailer-towing",
            title="拖拽挂车",
            questions=("可以拖挂车吗", "可以拖车吗", "能拖挂吗"),
            question_terms=("拖挂", "拖车", "拖拽"),
            source_terms=("拖拽挂车", "丰田公司建议不要使用", "不要使用丰田车拖拽挂车"),
            preferred_chapters=("4-1. 驾驶前",),
            max_source_pages=1,
        ),
        FaqBlueprint(
            id="flat-tire",
            title="爆胎或轮胎泄气",
            questions=("爆胎怎么办", "轮胎漏气怎么办", "轮胎泄气怎么办"),
            question_terms=("爆胎", "轮胎漏气", "轮胎泄气"),
            source_terms=("发生爆胎或轮胎突然泄气", "轮胎可能泄气", "泄气轮胎"),
            preferred_chapters=("7-2. 紧急情况下",),
        ),
    )

    def __init__(self, path: Path) -> None:
        self._path = path
        self._lock = RLock()
        self._entries: list[dict] = []
        self._load()

    def rebuild(self, items: list[dict]) -> None:
        """基于当前检索索引，为每个车型生成高频问题的证据页映射。"""
        grouped: dict[tuple[int, int, str], list[dict]] = {}
        for item in items:
            try:
                key = (
                    int(item["manualId"]),
                    int(item["vehicleId"]),
                    str(item["documentName"]),
                )
            except (KeyError, TypeError, ValueError):
                continue
            grouped.setdefault(key, []).append(item)

        entries: list[dict] = []
        for (manual_id, vehicle_id, document_name), pages in grouped.items():
            for blueprint in self._BLUEPRINTS:
                source_pages = self._select_source_pages(pages, blueprint)
                if not source_pages:
                    continue

                entries.append(
                    {
                        "id": blueprint.id,
                        "title": blueprint.title,
                        "manualId": manual_id,
                        "vehicleId": vehicle_id,
                        "documentName": document_name,
                        "questions": list(blueprint.questions),
                        "questionTerms": list(blueprint.question_terms),
                        "sourcePages": source_pages,
                    }
                )

        with self._lock:
            self._entries = entries
            self._save()

    def find_source_pages(self, vehicle_id: int, question: str) -> list[int]:
        """返回最贴近高频问法的本地 FAQ 证据页；不确定时返回空列表。"""
        normalized = self._normalize(question)
        if not normalized:
            return []

        with self._lock:
            candidates = [
                entry for entry in self._entries
                if entry.get("vehicleId") == vehicle_id
            ]

        best_score = 0
        best_pages: list[int] = []
        for entry in candidates:
            score = self._question_score(normalized, entry)
            if score > best_score:
                best_score = score
                best_pages = [
                    int(page) for page in entry.get("sourcePages", [])
                    if isinstance(page, int) or (isinstance(page, str) and page.isdigit())
                ]

        return best_pages if best_score >= 100 else []

    def _select_source_pages(self, pages: list[dict], blueprint: FaqBlueprint) -> list[int]:
        scored: list[tuple[int, int]] = []
        for page in pages:
            page_number = page.get("pdfPageNumber")
            if not isinstance(page_number, int):
                continue

            chapter = str(page.get("chapter", "")).lower()
            quote = str(page.get("quote", "")).lower()
            if self._is_navigation_page(chapter, quote):
                continue

            score = 0
            for term in blueprint.source_terms:
                normalized_term = term.lower()
                if normalized_term in chapter:
                    score += len(normalized_term) * 4
                if normalized_term in quote:
                    score += len(normalized_term)
            if score and any(
                preferred.lower() in chapter
                for preferred in blueprint.preferred_chapters
            ):
                score += 48
            if score:
                scored.append((score, page_number))

        scored.sort(reverse=True)
        return list(dict.fromkeys(page for _, page in scored))[:blueprint.max_source_pages]

    def _is_navigation_page(self, chapter: str, quote: str) -> bool:
        return (
            "字母索引" in quote
            or "图片索引" in quote
            or quote.count("...") >= 3
            or chapter == "用户手册"
        )

    def _question_score(self, question: str, entry: dict) -> int:
        score = 0
        for phrase in entry.get("questions", []):
            normalized_phrase = self._normalize(str(phrase))
            if normalized_phrase and normalized_phrase in question:
                score = max(score, 100 + len(normalized_phrase))

        matched_terms = [
            term for term in entry.get("questionTerms", [])
            if self._normalize(str(term)) in question
        ]
        if matched_terms:
            score = max(score, 100 + sum(len(term) for term in matched_terms))

        return score

    def _load(self) -> None:
        if not self._path.exists():
            return
        try:
            payload = json.loads(self._path.read_text(encoding="utf-8"))
            if isinstance(payload, list):
                self._entries = payload
        except (json.JSONDecodeError, OSError):
            self._entries = []

    def _save(self) -> None:
        self._path.parent.mkdir(parents=True, exist_ok=True)
        temporary_path = self._path.with_suffix(".tmp")
        temporary_path.write_text(
            json.dumps(self._entries, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        temporary_path.replace(self._path)

    def _normalize(self, text: str) -> str:
        return re.sub(r"[\s，。？！?!、：:（）()\-]", "", text).lower()
