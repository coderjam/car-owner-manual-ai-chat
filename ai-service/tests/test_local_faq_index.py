import json
import tempfile
import unittest
from pathlib import Path

from app.services.local_faq_index import LocalFaqIndex


class LocalFaqIndexTests(unittest.TestCase):
    def setUp(self) -> None:
        self.items = [
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "用户手册",
                "pdfPageNumber": 492,
                "quote": "字母索引。保养须知................353 保养计划................355",
            },
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "6-2. 保养",
                "pdfPageNumber": 353,
                "quote": "保养须知。请按照保养计划的规定间隔进行定期保养。",
            },
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "6-3. 自行保养",
                "pdfPageNumber": 367,
                "quote": "检查发动机机油。加注发动机机油前，请检查机油油位。",
            },
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "7-2. 紧急情况下应采取的措施",
                "pdfPageNumber": 415,
                "quote": "警告信息。请根据警告灯和警告信息采取相应的应对措施。",
            },
        ]

    def test_rebuild_creates_vehicle_specific_faq_entries(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "local_faq.json"
            index = LocalFaqIndex(path)

            index.rebuild(self.items)

            self.assertEqual(index.find_source_pages(1, "保养应该怎么做"), [353])
            self.assertEqual(index.find_source_pages(1, "机油怎么检查"), [367])
            self.assertEqual(index.find_source_pages(99, "保养应该怎么做"), [])
            saved_entries = json.loads(path.read_text(encoding="utf-8"))
            self.assertTrue(any(entry["id"] == "maintenance-overview" for entry in saved_entries))

    def test_question_must_match_a_known_faq_pattern(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            index = LocalFaqIndex(Path(directory) / "local_faq.json")
            index.rebuild(self.items)

            self.assertEqual(index.find_source_pages(1, "后排座椅怎么放倒"), [])


if __name__ == "__main__":
    unittest.main()
