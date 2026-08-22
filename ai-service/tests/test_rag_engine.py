import unittest
from unittest.mock import Mock

from app.models import ChatHistory, ChatRequest
from app.services.rag_engine import RagEngine


class RagEngineTests(unittest.TestCase):
    def setUp(self) -> None:
        self.engine = RagEngine()
        self.engine._items = [
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "驾驶辅助系统",
                "pdfPageNumber": 229,
                "printedPageNumber": 215,
                "quote": "PDA 主动驾驶辅助用于识别风险并提供辅助制动或转向支持。",
                "pageImageUrl": "/manuals/1/pages/229.svg",
                "pdfPageUrl": "",
            },
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "4-5. 使用驾驶辅助系统",
                "pdfPageNumber": 238,
                "printedPageNumber": 238,
                "quote": "PDA（主动驾驶辅助）会操作制动器和方向盘，以防车辆过于靠近对象。",
                "pageImageUrl": "/manuals/1/pages/238.webp",
                "pdfPageUrl": "",
            },
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "保养维护",
                "pdfPageNumber": 330,
                "printedPageNumber": 316,
                "quote": "长途驾驶前建议检查轮胎、机油、冷却液和灯光。",
                "pageImageUrl": "/manuals/1/pages/330.svg",
                "pdfPageUrl": "",
            },
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "3-3. 调节座椅",
                "pdfPageNumber": 142,
                "printedPageNumber": 142,
                "quote": "后排座椅靠背可以分别折叠，折叠前请收好安全带。",
                "pageImageUrl": "/manuals/1/pages/142.webp",
                "pdfPageUrl": "",
            },
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "用户手册",
                "pdfPageNumber": 3,
                "printedPageNumber": 3,
                "quote": "目录 PDA（主动驾驶辅助）........238",
                "pageImageUrl": "/manuals/1/pages/3.webp",
                "pdfPageUrl": "",
            },
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "4-5. 使用驾驶辅助系统",
                "pdfPageNumber": 241,
                "printedPageNumber": 241,
                "quote": "可通过定制设定启用/禁用主动驾驶辅助。更改主动驾驶辅助设定。",
                "pageImageUrl": "/manuals/1/pages/241.webp",
                "pdfPageUrl": "",
            },
        ]

    def test_search_returns_no_reference_when_nothing_matches(self) -> None:
        self.assertEqual(self.engine._search(1, "蓝牙设备如何改名"), [])

    def test_missing_manual_evidence_uses_labeled_general_ai_answer(self) -> None:
        self.engine._llm.generate_general_answer = Mock(
            return_value="通常可以在车辆设置中查找相关选项。"
        )

        response = self.engine.answer(
            ChatRequest(userId=1, vehicleId=1, question="如何设置手机蓝牙名称？")
        )

        self.assertEqual(response.references, [])
        self.assertTrue(response.answer.startswith("【AI 通用回答，不来自当前车辆用户手册】"))
        self.assertIn("通常可以", response.answer)

    def test_general_conversation_does_not_reuse_manual_references(self) -> None:
        response = self.engine.answer(
            ChatRequest(
                userId=1,
                vehicleId=1,
                question="你是谁？",
                recentHistory=[
                    ChatHistory(
                        id=1,
                        userId=1,
                        vehicleId=1,
                        question="PDA 是什么？",
                        answer="PDA 是主动驾驶辅助。",
                    )
                ],
            )
        )

        self.assertEqual(response.references, [])
        self.assertIn("汽车 AI 用户手册助手", response.answer)

    def test_capability_question_is_answered_without_manual_references(self) -> None:
        response = self.engine.answer(
            ChatRequest(userId=1, vehicleId=1, question="你可以干什么？")
        )

        self.assertEqual(response.references, [])
        self.assertIn("汽车 AI 用户手册助手", response.answer)

    def test_search_returns_the_matching_manual_page(self) -> None:
        matches = self.engine._search(1, "PDA 怎么开启")

        self.assertEqual(matches[0]["pdfPageNumber"], 241)
        self.assertEqual(len(matches), 1)

    def test_ai_evidence_judge_can_remove_weak_references(self) -> None:
        self.engine._llm.assess_evidence = Mock(return_value=False)

        response = self.engine.answer(
            ChatRequest(userId=1, vehicleId=1, question="PDA 怎么开启")
        )

        self.assertEqual(response.references, [])
        self.assertIn("没有找到足以回答", response.answer)

    def test_definition_question_prefers_the_definition_page(self) -> None:
        matches = self.engine._search(1, "PDA 是什么？")

        self.assertEqual(matches[0]["pdfPageNumber"], 238)

    def test_cited_page_is_added_to_the_visible_references(self) -> None:
        references = [self.engine._to_reference(self.engine._items[0])]

        enriched = self.engine._include_cited_pages(
            "详细说明参见手册第238页。",
            references,
            vehicle_id=1,
        )

        self.assertIn(238, [reference.pdfPageNumber for reference in enriched])

    def test_search_understands_an_unsegmented_chinese_question(self) -> None:
        matches = self.engine._search(1, "后排座椅怎么放倒")

        self.assertEqual(matches[0]["pdfPageNumber"], 142)

    def test_maintenance_question_finds_the_maintenance_page(self) -> None:
        matches = self.engine._search(1, "保养怎么做")

        self.assertEqual(matches[0]["pdfPageNumber"], 330)

    def test_broad_maintenance_question_prefers_the_overview_page(self) -> None:
        self.engine._items.extend(
            [
                {
                    "manualId": 1,
                    "vehicleId": 1,
                    "documentName": "2026 凯美瑞用户手册.pdf",
                    "chapter": "6-2. 保养",
                    "pdfPageNumber": 353,
                    "printedPageNumber": 353,
                    "quote": (
                        "保养须知。请按照保养计划的规定间隔进行定期保养，"
                        "定期保养间隔根据里程表读数或时间间隔而定，以先达到者为准。"
                    ),
                    "pageImageUrl": "/manuals/1/pages/353.webp",
                    "pdfPageUrl": "",
                },
                {
                    "manualId": 1,
                    "vehicleId": 1,
                    "documentName": "2026 凯美瑞用户手册.pdf",
                    "chapter": "6-3. 自行保养",
                    "pdfPageNumber": 367,
                    "printedPageNumber": 367,
                    "quote": "加注发动机机油后，应重置发动机机油保养数据。",
                    "pageImageUrl": "/manuals/1/pages/367.webp",
                    "pdfPageUrl": "",
                },
            ]
        )

        matches = self.engine._search(1, "保养应该怎么做")

        self.assertEqual(matches[0]["pdfPageNumber"], 353)

    def test_overview_evidence_is_not_rejected_for_broad_maintenance(self) -> None:
        self.engine._items.append(
            {
                "manualId": 1,
                "vehicleId": 1,
                "documentName": "2026 凯美瑞用户手册.pdf",
                "chapter": "6-2. 保养",
                "pdfPageNumber": 353,
                "printedPageNumber": 353,
                "quote": "保养须知。请按照保养计划的规定间隔进行定期保养。",
                "pageImageUrl": "/manuals/1/pages/353.webp",
                "pdfPageUrl": "",
            }
        )
        self.engine._llm.assess_evidence = Mock(return_value=False)
        self.engine._llm.generate_answer = Mock(return_value="请按照保养计划定期保养。")

        response = self.engine.answer(
            ChatRequest(userId=1, vehicleId=1, question="保养应该怎么做")
        )

        self.assertTrue(response.answer.startswith("【来自当前车辆用户手册】"))
        self.assertIn("请按照保养计划定期保养。", response.answer)
        self.assertEqual(response.references[0].pdfPageNumber, 353)

    def test_specific_maintenance_question_can_still_be_rejected(self) -> None:
        self.engine._llm.assess_evidence = Mock(return_value=False)

        response = self.engine.answer(
            ChatRequest(userId=1, vehicleId=1, question="机油保养应该怎么做")
        )

        self.assertEqual(response.references, [])
        self.assertIn("没有找到足以回答", response.answer)

    def test_colloquial_maintenance_question_uses_the_same_intent(self) -> None:
        matches = self.engine._search(1, "车子平时怎么照顾")

        self.assertEqual(matches[0]["pdfPageNumber"], 330)

    def test_unknown_vehicle_does_not_reset_existing_index(self) -> None:
        original_items = list(self.engine._items)

        self.assertEqual(self.engine._search(99, "PDA 怎么开启"), [])
        self.assertEqual(self.engine._items, original_items)

    def test_follow_up_uses_only_the_current_conversation_context(self) -> None:
        request = ChatRequest(
            userId=1,
            vehicleId=1,
            conversationId="conversation-a",
            question="高速可以使用吗？",
            recentHistory=[
                ChatHistory(
                    id=1,
                    userId=1,
                    vehicleId=1,
                    conversationId="conversation-a",
                    question="PDA 是什么？",
                    answer="PDA 是主动驾驶辅助。",
                )
            ],
        )

        retrieval_question = self.engine._build_retrieval_question(request)
        matches = self.engine._search(1, retrieval_question)

        self.assertEqual(matches[0]["pdfPageNumber"], 238)

        self.engine._llm.api_key = ""
        response = self.engine.answer(request)
        self.assertIn("没有明确说明", response.answer)


if __name__ == "__main__":
    unittest.main()
