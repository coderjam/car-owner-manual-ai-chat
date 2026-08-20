import json
import tempfile
import unittest
from pathlib import Path

from app.models import ManualPage, ProcessManualResult
from app.services.manual_processor import ManualProcessor


class ManualProcessorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.processor = ManualProcessor()

    def test_empty_page_does_not_create_a_knowledge_chunk(self) -> None:
        self.assertEqual(self.processor._split_text(""), [])

    def test_long_text_is_split_within_the_requested_limit(self) -> None:
        chunks = self.processor._split_text("A" * 950 + "\n" + "B" * 950, max_chars=900)

        self.assertGreater(len(chunks), 1)
        self.assertTrue(all(len(chunk) <= 900 for chunk in chunks))
        self.assertEqual("".join(chunks), "A" * 950 + "B" * 950)

    def test_page_number_can_be_read_from_the_header(self) -> None:
        text = "229\n4-5. 使用驾驶辅助系统\n驾驶"

        self.assertEqual(self.processor._infer_printed_page_number(text), 229)

    def test_clean_text_removes_page_number_duplicate_header_and_font_glyphs(self) -> None:
        text = "229\n6-1. 保养和维护\n6-1. 保养和维护\n\uf06c检查轮胎\n\uf0aeP.373"

        cleaned = self.processor._clean_text(text)

        self.assertNotIn("229", cleaned)
        self.assertEqual(cleaned.count("6-1. 保养和维护"), 1)
        self.assertIn("•检查轮胎", cleaned)
        self.assertIn("→P.373", cleaned)

    def test_numbered_section_is_used_as_the_chapter(self) -> None:
        text = "229\n4\n4-5. 使用驾驶辅助系统\n驾驶"

        self.assertEqual(self.processor._infer_chapter(text), "4-5. 使用驾驶辅助系统")

    def test_manifest_is_saved_next_to_the_pages_directory(self) -> None:
        result = ProcessManualResult(
            totalPages=1,
            generatedPageImages=1,
            knowledgeChunks=1,
            pages=[
                ManualPage(
                    manualId=1,
                    pdfPageNumber=1,
                    printedPageNumber=1,
                    chapter="用户手册",
                    pageText="测试页面",
                    pageImageUrl="/manuals/1/pages/1.webp",
                )
            ],
        )

        with tempfile.TemporaryDirectory() as directory:
            pages_directory = Path(directory) / "pages"
            pages_directory.mkdir()

            self.processor.save_manifest(result, str(pages_directory))
            manifest = json.loads((Path(directory) / "manifest.json").read_text())

        self.assertEqual(manifest["totalPages"], 1)
        self.assertEqual(manifest["pages"][0]["printedPageNumber"], 1)


if __name__ == "__main__":
    unittest.main()
