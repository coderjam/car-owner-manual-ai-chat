from __future__ import annotations

import os
import logging
from pathlib import Path

import httpx
from dotenv import load_dotenv

from app.models import ChatHistory, KnowledgeReference

load_dotenv(Path(__file__).resolve().parents[3] / ".env")

logger = logging.getLogger(__name__)


class LlmClient:
    """大模型调用适配器。

    为了让 V1 不绑定某一家模型厂商，这里使用 OpenAI-compatible Chat Completions
    协议。通义千问兼容模式、DeepSeek 和 OpenAI 都可以使用同一套请求结构。

    环境变量：
    - LLM_API_KEY：模型 API Key。为空时不调用远程模型。
    - LLM_BASE_URL：DeepSeek 根地址或完整 Chat Completions 地址。
    - LLM_MODEL：模型名称，默认 deepseek-v4-flash。
    - LLM_THINKING：DeepSeek 思考模式，默认 disabled，适合手册问答。
    """

    def __init__(self) -> None:
        self.api_key = os.getenv("LLM_API_KEY", "").strip()
        self.base_url = os.getenv(
            "LLM_BASE_URL",
            "https://api.deepseek.com",
        )
        self.model = os.getenv("LLM_MODEL", "deepseek-v4-flash")
        self.thinking = os.getenv("LLM_THINKING", "disabled").strip().lower()
        self.evidence_judge_enabled = os.getenv("LLM_EVIDENCE_JUDGE", "true").lower() == "true"
        self.timeout_seconds = self._read_timeout()

    @property
    def enabled(self) -> bool:
        return bool(self.api_key)

    def generate_answer(
        self,
        question: str,
        references: list[KnowledgeReference],
        recent_history: list[ChatHistory] | None = None,
    ) -> str | None:
        if not self.enabled or not references:
            return None

        context = "\n\n".join(
            [
                (
                    f"文档：{source.documentName}\n"
                    f"章节：{source.chapter}\n"
                    f"PDF页码：{source.pdfPageNumber}\n"
                    f"手册页码：{source.printedPageNumber or source.pdfPageNumber}\n"
                    f"原文：{source.quote}"
                )
                for source in references
            ]
        )

        messages = [
            {
                "role": "system",
                "content": (
                    "你是汽车用户手册智能助手。"
                    "请只根据本次提供的用户手册资料回答。"
                    "历史对话仅用于理解用户的指代，不能作为事实依据。"
                    "如果资料没有明确说明，请直接说明未找到明确依据。"
                    "只能引用本次手册资料中出现的页码；如果回答提到第几页，"
                    "必须确保该页属于本次提供的资料，不要自行补充资料外的页码。"
                    "回答要简洁，并提醒用户以车辆实际提示和官方手册为准。"
                ),
            }
        ]

        for item in (recent_history or [])[-4:]:
            messages.extend(
                [
                    {"role": "user", "content": item.question},
                    {"role": "assistant", "content": item.answer},
                ]
            )

        messages.append(
            {
                "role": "user",
                "content": f"本次手册资料：\n{context}\n\n当前问题：{question}",
            }
        )

        payload = {
            "model": self.model,
            "temperature": 0.2,
            "messages": messages,
        }

        if self.model.startswith("deepseek-"):
            payload["thinking"] = {
                "type": "enabled" if self.thinking == "enabled" else "disabled"
            }

        try:
            response = httpx.post(
                self._chat_completions_url(),
                headers={"Authorization": f"Bearer {self.api_key}"},
                json=payload,
                timeout=self.timeout_seconds,
            )
            response.raise_for_status()
            data = response.json()
            content = data.get("choices", [{}])[0].get("message", {}).get("content")
            return content.strip() if isinstance(content, str) and content.strip() else None
        except Exception as exception:
            logger.warning("LLM request failed: %s", type(exception).__name__)
            return None

    def generate_general_answer(self, question: str) -> str | None:
        """在手册没有足够依据时，生成明确标注为 AI 的通用回答。"""
        if not self.enabled:
            return None

        messages = [
            {
                "role": "system",
                "content": (
                    "你是汽车相关的通用 AI 助手。当前没有检索到足够的本车型用户手册依据。"
                    "请基于通用知识回答，但不要声称内容来自当前车辆用户手册，"
                    "不要编造页码、章节或手册引用。对于安全、维修和驾驶问题，"
                    "请提醒用户以车辆实际提示和官方手册为准。回答简洁直接。"
                ),
            },
            {"role": "user", "content": question},
        ]
        payload = {
            "model": self.model,
            "temperature": 0.2,
            "messages": messages,
        }

        if self.model.startswith("deepseek-"):
            payload["thinking"] = {
                "type": "enabled" if self.thinking == "enabled" else "disabled"
            }

        try:
            response = httpx.post(
                self._chat_completions_url(),
                headers={"Authorization": f"Bearer {self.api_key}"},
                json=payload,
                timeout=self.timeout_seconds,
            )
            response.raise_for_status()
            data = response.json()
            content = data.get("choices", [{}])[0].get("message", {}).get("content")
            return content.strip() if isinstance(content, str) and content.strip() else None
        except Exception as exception:
            logger.warning("General LLM request failed: %s", type(exception).__name__)
            return None

    def assess_evidence(
        self,
        question: str,
        references: list[KnowledgeReference],
    ) -> bool | None:
        """让大模型判断当前片段是否足以回答问题。

        返回 None 表示没有配置模型或判断请求失败，此时由本地相关性阈值兜底。
        """
        if not self.enabled or not self.evidence_judge_enabled or not references:
            return None

        context = "\n\n".join(
            f"章节：{source.chapter}\n原文：{source.quote}"
            for source in references
        )
        messages = [
            {
                "role": "system",
                "content": (
                    "你是汽车用户手册证据审查器。判断提供的手册原文是否足以直接回答用户问题。"
                    "如果用户询问宽泛的保养、功能或操作概览，而原文提供了对应的总览、计划或原则，"
                    "应判定为 SUPPORTED；回答可以限定在原文覆盖的范围内，并引导用户继续询问具体项目。"
                    "只输出 SUPPORTED 或 UNSUPPORTED，不要输出其他内容。"
                ),
            },
            {
                "role": "user",
                "content": f"用户问题：{question}\n手册原文：\n{context}",
            },
        ]
        payload = {"model": self.model, "temperature": 0, "messages": messages}
        if self.model.startswith("deepseek-"):
            payload["thinking"] = {
                "type": "enabled" if self.thinking == "enabled" else "disabled"
            }

        try:
            response = httpx.post(
                self._chat_completions_url(),
                headers={"Authorization": f"Bearer {self.api_key}"},
                json=payload,
                timeout=self.timeout_seconds,
            )
            response.raise_for_status()
            content = response.json().get("choices", [{}])[0].get("message", {}).get("content", "")
            normalized = content.strip().upper()
            if "UNSUPPORTED" in normalized:
                return False
            if "SUPPORTED" in normalized:
                return True
        except Exception as exception:
            logger.warning("Evidence judge failed: %s", type(exception).__name__)

        return None

    def _chat_completions_url(self) -> str:
        base_url = self.base_url.rstrip("/")
        if base_url.endswith("/chat/completions"):
            return base_url

        return f"{base_url}/chat/completions"

    def _read_timeout(self) -> float:
        try:
            return max(float(os.getenv("LLM_TIMEOUT_SECONDS", "60")), 1)
        except ValueError:
            return 60
