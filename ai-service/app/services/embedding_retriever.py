from __future__ import annotations

import os
from typing import Any

import numpy as np


class EmbeddingRetriever:
    """可选的本地 Embedding 检索器。

    sentence-transformers 未安装或模型加载失败时返回 None，RAG 会自动使用
    关键词检索，不影响 V1 本地启动。安装模型后无需修改检索接口即可启用。
    """

    def __init__(self) -> None:
        self.model: Any = None
        self.vectors: np.ndarray | None = None
        self._items: list[dict] = []
        if os.getenv("RAG_EMBEDDING_ENABLED", "false").lower() != "true":
            return

        try:
            from sentence_transformers import SentenceTransformer

            model_name = os.getenv("EMBEDDING_MODEL", "BAAI/bge-small-zh-v1.5")
            self.model = SentenceTransformer(model_name)
        except Exception:
            self.model = None

    @property
    def enabled(self) -> bool:
        return self.model is not None

    def fit(self, items: list[dict]) -> None:
        self._items = list(items)
        if not self.enabled or not items:
            self.vectors = None
            return

        texts = [self._text(item) for item in items]
        self.vectors = self.model.encode(texts, normalize_embeddings=True)

    def search(self, question: str, top_k: int = 6) -> list[tuple[float, dict]]:
        if not self.enabled or self.vectors is None or not self._items:
            return []

        query = self.model.encode([question], normalize_embeddings=True)[0]
        scores = np.asarray(self.vectors) @ np.asarray(query)
        indexes = np.argsort(scores)[::-1][:top_k]
        return [(float(scores[index]), self._items[index]) for index in indexes]

    @staticmethod
    def _text(item: dict) -> str:
        return f"{item.get('chapter', '')}\n{item.get('quote', '')}"
