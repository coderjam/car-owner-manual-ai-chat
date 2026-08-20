import os
import unittest
from unittest.mock import patch

from app.models import KnowledgeReference
from app.services.llm_client import LlmClient


class FakeResponse:
    def raise_for_status(self) -> None:
        return None

    def json(self) -> dict:
        return {
            "choices": [
                {"message": {"content": "请根据第 241 页设置。"}}
            ]
        }


class LlmClientTests(unittest.TestCase):
    def test_deepseek_root_url_is_normalized_and_thinking_can_be_disabled(self) -> None:
        reference = KnowledgeReference(
            documentId=1,
            documentName="用户手册.pdf",
            chapter="驾驶辅助系统",
            pdfPageNumber=241,
            printedPageNumber=241,
            quote="可通过定制设定启用或禁用主动驾驶辅助。",
            pageImageUrl="/manuals/1/pages/241.webp",
            pdfPageUrl="/manuals/1/original.pdf#page=241",
        )

        with patch.dict(
            os.environ,
            {
                "LLM_API_KEY": "test-key",
                "LLM_BASE_URL": "https://api.deepseek.com",
                "LLM_MODEL": "deepseek-v4-flash",
                "LLM_THINKING": "disabled",
            },
            clear=False,
        ), patch("app.services.llm_client.httpx.post", return_value=FakeResponse()) as post:
            client = LlmClient()
            answer = client.generate_answer("PDA 怎么开启", [reference])

        self.assertEqual(answer, "请根据第 241 页设置。")
        self.assertEqual(
            post.call_args.args[0],
            "https://api.deepseek.com/chat/completions",
        )
        self.assertEqual(
            post.call_args.kwargs["json"]["thinking"],
            {"type": "disabled"},
        )


if __name__ == "__main__":
    unittest.main()
