from __future__ import annotations

import json
import re
from threading import RLock
from datetime import datetime, timezone
from pathlib import Path

from app.models import ChatRequest, ChatResponse, KnowledgeReference, ManualPage
from app.services.embedding_retriever import EmbeddingRetriever
from app.services.intent_classifier import AutomotiveIntentClassifier
from app.services.local_faq_index import LocalFaqIndex
from app.services.llm_client import LlmClient


class RagEngine:
    """汽车用户手册 RAG 检索引擎。

    这个类先用关键词相似度跑通完整链路：问题 -> 检索 -> 生成答案 -> 返回引用。
    Embedding 可选启用；未启用时使用同一套意图扩展后的关键词检索。
    """

    def __init__(self) -> None:
        self._index_path = Path(__file__).resolve().parents[2] / "data" / "index.json"
        self._index_path.parent.mkdir(parents=True, exist_ok=True)
        self._items: list[dict] = []
        self._lock = RLock()
        self._llm = LlmClient()
        self._intent_classifier = AutomotiveIntentClassifier()
        self._embedding = EmbeddingRetriever()
        self._faq = LocalFaqIndex(self._index_path.parent / "local_faq.json")
        self._load()
        if self._items:
            self._faq.rebuild(self._items)
        self._embedding.fit(self._items)

    def answer(self, request: ChatRequest) -> ChatResponse:
        if self._is_general_conversation(request.question):
            return ChatResponse(
                answer=(
                    "我是汽车 AI 用户手册助手。\n"
                    "我可以根据当前车型的官方手册，帮你查询功能说明、操作方法、驾驶辅助、"
                    "故障提示和使用注意事项，并提供对应章节、页码和 PDF 原页。"
                    "如果手册中没有明确依据，我也会直接告诉你。"
                ),
                references=[],
                createTime=self._now(),
            )

        direct_matches = self._search_local_faq(request.vehicleId, request.question)
        if not direct_matches:
            direct_matches = self._search(request.vehicleId, request.question)
        matches = direct_matches

        if not matches and request.recentHistory:
            retrieval_question = self._build_retrieval_question(request)
            matches = self._search(request.vehicleId, retrieval_question)

        if not matches:
            return self._general_ai_fallback(
                request,
                "当前车辆用户手册中没有找到与这个问题相关的明确内容。",
            )

        references = [self._to_reference(item) for item in matches[:3]]
        evidence_supported = self._llm.assess_evidence(request.question, references)
        if (
            evidence_supported is False
            and not self._has_maintenance_overview_evidence(
                request.question,
                references,
            )
        ):
            return self._general_ai_fallback(
                request,
                "当前车辆用户手册中没有找到足以回答这个问题的明确依据。",
            )

        llm_answer = self._llm.generate_answer(
            request.question,
            references,
            request.recentHistory,
        )

        if llm_answer:
            references = self._include_cited_pages(
                llm_answer,
                references,
                request.vehicleId,
            )
            return ChatResponse(
                answer=f"【来自当前车辆用户手册】\n{llm_answer}",
                references=references,
                createTime=self._now(),
            )

        first = references[0]
        display_page = first.printedPageNumber or first.pdfPageNumber

        answer = (
            f"【来自当前车辆用户手册】\n根据《{first.documentName}》{first.chapter}第 {display_page} 页，"
            f"手册相关内容说明：{self._compact_quote(first.quote)} "
            "请以下方 PDF 整页图片中的手册原文为准。"
        )

        if not direct_matches:
            answer = (
                f"【来自当前车辆用户手册】\n结合前文定位到《{first.documentName}》{first.chapter}第 {display_page} 页，"
                f"但当前手册片段没有明确说明“{request.question}”的具体条件。"
                f"相关原文：{self._compact_quote(first.quote)}"
            )

        return ChatResponse(
            answer=answer,
            references=references,
            createTime=self._now(),
        )

    def _general_ai_fallback(
        self,
        request: ChatRequest,
        reason: str,
    ) -> ChatResponse:
        answer = self._llm.generate_general_answer(request.question)
        if answer:
            return ChatResponse(
                answer=f"【AI 通用回答，不来自当前车辆用户手册】\n{answer}",
                references=[],
                createTime=self._now(),
            )

        return ChatResponse(
            answer=f"{reason}\nAI 通用回答服务当前不可用，请稍后重试。",
            references=[],
            createTime=self._now(),
        )

    def _is_general_conversation(self, question: str) -> bool:
        normalized = re.sub(r"[\s，。？！?!、]+", "", question).lower()
        return bool(
            re.fullmatch(
                r"(?:你是谁|你是什么|你能做什么|你会做什么|你可以干什么|你可以做什么|能做什么|你好|嗨|哈喽|谢谢|感谢|再见)(?:呀|啊|呢)?",
                normalized,
            )
        )

    def index_pages(
        self,
        manual_id: int,
        vehicle_id: int,
        document_name: str,
        pages: list[ManualPage],
    ) -> None:
        with self._lock:
            self._items = [
                item for item in self._items
                if not (item["manualId"] == manual_id and item["vehicleId"] == vehicle_id)
            ]

            for page in pages:
                if not page.pageText:
                    continue

                self._items.append(
                    {
                        "manualId": manual_id,
                        "vehicleId": vehicle_id,
                        "documentName": document_name,
                        "chapter": page.chapter or "用户手册",
                        "pdfPageNumber": page.pdfPageNumber,
                        "printedPageNumber": page.printedPageNumber,
                        "quote": page.pageText[:4000],
                        "pageImageUrl": page.pageImageUrl,
                        "pdfPageUrl": f"/manuals/{manual_id}/original.pdf#page={page.pdfPageNumber}",
                    }
                )

            self._save()
            self._faq.rebuild(self._items)
            self._embedding.fit(self._items)

    def _search_local_faq(self, vehicle_id: int, question: str) -> list[dict]:
        """高频问法优先使用本地 FAQ 已训练出的手册页。"""
        page_numbers = self._faq.find_source_pages(vehicle_id, question)
        if not page_numbers:
            return []

        with self._lock:
            by_page_number = {
                item["pdfPageNumber"]: item
                for item in self._items
                if item["vehicleId"] == vehicle_id
            }
        return [
            by_page_number[page_number]
            for page_number in page_numbers
            if page_number in by_page_number
        ]

    def _search(self, vehicle_id: int, question: str) -> list[dict]:
        with self._lock:
            candidates = [
                item for item in self._items
                if item["vehicleId"] == vehicle_id
            ]

        semantic_scores = {
            id(item): score
            for score, item in self._embedding.search(question)
            if item.get("vehicleId") == vehicle_id
        }
        scored = []
        for item in candidates:
            lexical_score = self._score(question, item)
            semantic_score = semantic_scores.get(id(item), 0.0)
            # 只有相似度超过基础线才参与融合，避免向量模型把泛化页面全部拉进来。
            embedding_bonus = max(0, int((semantic_score - 0.35) * 100))
            scored.append((lexical_score + embedding_bonus, item))

        scored.sort(key=lambda pair: pair[0], reverse=True)
        positive_matches = [(score, item) for score, item in scored if score > 0]
        if not positive_matches:
            return []

        confidence_floor = self._confidence_floor(question)
        best_score = positive_matches[0][0]

        # 只命中“黄色三角”中的“三角”这类弱匹配不能作为回答依据。
        # 同时保留与最佳结果接近的连续正文页，便于多页说明一起返回。
        if best_score < max(confidence_floor, 20):
            return []

        relative_floor = max(confidence_floor, int(best_score * 0.8))
        return [
            item for score, item in positive_matches
            if score >= relative_floor
        ][:3]

    def _build_retrieval_question(self, request: ChatRequest) -> str:
        recent_questions = [
            item.question
            for item in request.recentHistory[-2:]
            if item.question.strip()
        ]
        return " ".join([*recent_questions, request.question])

    def _score(self, question: str, item: dict) -> int:
        chapter = item["chapter"].lower()
        quote = item["quote"].lower()
        keywords = self._tokens(question)
        score = 0

        for keyword in keywords:
            normalized = keyword.lower()
            weight = min(len(normalized), 8) ** 2
            if normalized in chapter:
                score += weight * 3
            elif normalized in quote:
                score += weight

        if (
            any(keyword in question for keyword in ["开", "开启", "打开", "启用", "关闭"])
            and "主动驾驶辅助" in quote
            and any(keyword in quote for keyword in ["启用", "禁用", "设定"])
        ):
            score += 128

        if (
            any(keyword in question for keyword in ["高速", "速度", "车速", "条件"])
            and "系统工作时的车速" in quote
        ):
            score += 128

        if (
            any(keyword in question for keyword in ["保养", "维护", "照顾"])
            and any(keyword in chapter or keyword in quote for keyword in ["保养", "维护"])
        ):
            score += 64

        # “保养应该怎么做”问的是整体方案，不应被某个恰好重复出现
        # “保养”一词的具体操作页（例如加注机油）抢占首位。
        # 优先返回保养须知、计划说明和项目表，后续再由回答引导用户
        # 针对机油、轮胎等具体项目继续提问。
        if self._is_broad_maintenance_question(question):
            if "保养须知" in quote and "按照保养计划" in quote:
                score += 320
            elif "保养计划须知" in quote and "保养操作" in quote:
                score += 280
            elif "保养间隔" in quote and "发动机基本部件" in quote:
                score += 260

        if (
            any(keyword in question for keyword in ["是什么", "含义", "作用", "介绍"])
            and "pda" in question.lower()
        ):
            if "PDA（主动驾驶辅助）" in item["quote"]:
                score += 96
            if "会操作制动器和方向盘" in item["quote"]:
                score += 128

        # 目录页适合导航，不适合作为最终回答的首要依据。正文存在命中时，
        # 让具体操作页、条件页和警告页自然排到前面。
        if (
            ("目录" in quote and item["chapter"] == "用户手册")
            or "..." in item["chapter"]
            or quote.count("...") >= 3
        ):
            score //= 4

        return score

    def _is_broad_maintenance_question(self, question: str) -> bool:
        intents = self._intent_classifier.classify(question)
        has_maintenance_intent = any(
            intent.name == "maintenance"
            for intent in intents
        )
        if not has_maintenance_intent:
            return False

        specific_topics = (
            "机油", "滤清器", "冷却液", "轮胎", "胎压", "制动", "刹车",
            "蓄电池", "电池", "空调", "保险丝", "雨刷", "刮水器", "火花塞",
            "变速器", "喷洗液", "车灯", "灯泡", "发动机舱", "洗车", "内饰",
        )
        return not any(topic in question for topic in specific_topics)

    def _has_maintenance_overview_evidence(
        self,
        question: str,
        references: list[KnowledgeReference],
    ) -> bool:
        """识别可回答宽泛保养问题的手册总览证据。

        大模型证据审查有时会把“请给一个整体保养方案”理解成必须覆盖
        每个零件的完整操作步骤。手册的保养须知和计划页已经足以回答
        这类总览问题，因此只对明确命中总览页的情况进行本地兜底。
        """
        if not self._is_broad_maintenance_question(question):
            return False

        return any(
            "保养须知" in reference.quote
            and "按照保养计划" in reference.quote
            for reference in references
        )

    def _include_cited_pages(
        self,
        answer: str,
        references: list[KnowledgeReference],
        vehicle_id: int,
    ) -> list[KnowledgeReference]:
        cited_page_numbers = {
            int(number)
            for number in re.findall(r"(?:第\s*|P\.?\s*)(\d{1,4})\s*页?", answer, re.IGNORECASE)
        }

        if not cited_page_numbers:
            return references

        existing_pages = {
            reference.pdfPageNumber
            for reference in references
        }
        existing_pages.update(
            reference.printedPageNumber
            for reference in references
            if reference.printedPageNumber is not None
        )

        with self._lock:
            candidates = [
                item for item in self._items
                if item["vehicleId"] == vehicle_id
                and (
                    item["pdfPageNumber"] in cited_page_numbers
                    or item.get("printedPageNumber") in cited_page_numbers
                )
            ]

        for item in candidates:
            page_number = item["pdfPageNumber"]
            if page_number in existing_pages:
                continue

            references.append(self._to_reference(item))
            existing_pages.add(page_number)

            if len(references) >= 6:
                break

        return references

    def _tokens(self, text: str) -> list[str]:
        stop_tokens = {
            "怎么", "如何", "什么", "是否", "可以", "一下", "请问", "这个",
            "哪个", "车辆", "汽车", "用户", "手册", "功能", "使用", "进行",
            "相关", "设备", "系统", "显示", "操作",
        }
        tokens: list[str] = []

        for ascii_token in re.findall(r"[A-Za-z][A-Za-z0-9_-]+", text):
            tokens.append(ascii_token)

        for sequence in re.findall(r"[\u4e00-\u9fff]+", text):
            contains_question_word = any(token in sequence for token in stop_tokens)
            if (
                2 <= len(sequence) <= 12
                and sequence not in stop_tokens
                and not contains_question_word
            ):
                tokens.append(sequence)

            # 中文问题通常没有空格，用 2-4 字符片段覆盖“后排座椅怎么放倒”
            # 这类自然表达，同时用长度权重让更具体的词优先命中。
            for size in (4, 3, 2):
                for start in range(max(len(sequence) - size + 1, 0)):
                    token = sequence[start:start + size]
                    if token not in stop_tokens:
                        tokens.append(token)

        normalized_text = text.lower()

        # 首批车型使用的高频缩写与手册正文名称并不总在同一页出现。
        # 在检索阶段做可解释的术语扩展，仍然只从官方手册内容中取答案。
        if "pda" in normalized_text or "主动驾驶辅助" in text:
            tokens.append("主动驾驶辅助")

            if any(keyword in text for keyword in ["开", "开启", "打开", "启用", "关闭"]):
                tokens.extend(["更改主动驾驶辅助设定", "启用", "禁用", "设定"])

            if any(keyword in text for keyword in ["高速", "速度", "车速", "条件"]):
                tokens.extend(["系统工作时的车速", "系统工作条件"])

        if any(keyword in text for keyword in ["开启", "打开", "启用"]):
            tokens.extend(["启用", "开关", "设定"])

        if any(keyword in text for keyword in ["保养", "维护", "照顾"]):
            tokens.extend([
                "保养",
                "维护",
                "保养和维护",
                "保养须知",
                "定期保养",
                "自行保养",
                "保养数据",
            ])

        if any(keyword in text for keyword in ["没气", "漏气", "爆胎", "扎胎"]):
            tokens.extend(["轮胎泄气", "轮胎气压", "轮胎"])

        if any(keyword in text for keyword in ["黄三角", "故障灯", "指示灯", "警告灯"]):
            tokens.extend(["警告灯", "警告信息", "警告蜂鸣器", "指示灯"])

        if any(keyword in text for keyword in ["空调不凉", "不制冷", "冷气", "空调"]):
            tokens.extend(["空调", "空调滤清器", "温度调节"])

        if any(keyword in text for keyword in ["启动不了", "打不着", "无法启动", "不能启动"]):
            tokens.extend(["混合动力系统不能起动", "起动混合动力系统", "电源开关"])

        if any(keyword in text for keyword in ["加油", "加不了油", "油箱盖"]):
            tokens.extend(["加油方法", "燃油加注口盖", "燃油箱容量"])

        tokens.extend(self._intent_classifier.expand_terms(text))

        return list(dict.fromkeys(tokens))

    def _confidence_floor(self, text: str) -> int:
        lengths = [
            min(len(token), 4)
            for token in re.findall(r"[A-Za-z][A-Za-z0-9_-]+|[\u4e00-\u9fff]+", text)
            if len(token) >= 2
        ]
        return max((length ** 2 for length in lengths), default=1)

    def _to_reference(self, item: dict) -> KnowledgeReference:
        total_pages = max(
            (page.get("pdfPageNumber", 0) for page in self._items if page.get("manualId") == item["manualId"]),
            default=0,
        )
        return KnowledgeReference(
            documentId=item["manualId"],
            documentName=item["documentName"],
            chapter=item["chapter"],
            pdfPageNumber=item["pdfPageNumber"],
            printedPageNumber=item.get("printedPageNumber"),
            quote=item["quote"],
            pageImageUrl=item["pageImageUrl"],
            pdfPageUrl=item["pdfPageUrl"],
            totalPages=total_pages,
        )

    def _compact_quote(self, quote: str) -> str:
        text = re.sub(r"\s+", " ", quote).strip()
        return text[:180] + ("……" if len(text) > 180 else "")

    def _load(self) -> None:
        if not self._index_path.exists():
            self._seed_demo_index()
            return

        try:
            self._items = json.loads(self._index_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            self._seed_demo_index()

    def _save(self) -> None:
        temporary_path = self._index_path.with_suffix(".tmp")
        temporary_path.write_text(
            json.dumps(self._items, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        temporary_path.replace(self._index_path)

    def _seed_demo_index(self) -> None:
        self._items = []
        self._save()

    def _now(self) -> str:
        return datetime.now(timezone.utc).isoformat()
