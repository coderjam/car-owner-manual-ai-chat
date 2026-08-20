from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Intent:
    name: str
    terms: tuple[str, ...]


class AutomotiveIntentClassifier:
    """用可解释的汽车领域意图规则扩展用户口语。

    它不是最终的语义模型，但可以在 Embedding 不可用时稳定工作，
    并为后续训练/替换分类模型保留统一的输出结构。
    """

    _rules = (
        ("maintenance", ("保养", "维护", "照顾"), ("保养", "保养和维护", "定期保养", "自行保养", "保养须知")),
        ("tire_failure", ("没气", "漏气", "爆胎", "扎胎"), ("轮胎泄气", "轮胎气压", "轮胎")),
        ("warning_light", ("黄三角", "故障灯", "指示灯", "警告灯"), ("警告灯", "警告信息", "警告蜂鸣器", "指示灯")),
        ("air_conditioning", ("空调不凉", "不制冷", "冷气", "空调"), ("空调", "空调滤清器", "温度调节")),
        ("start_failure", ("启动不了", "打不着", "无法启动", "不能启动"), ("混合动力系统不能起动", "起动混合动力系统", "电源开关")),
        ("refueling", ("加油", "加不了油", "油箱盖"), ("加油方法", "燃油加注口盖", "燃油箱容量")),
        ("driver_assistance", ("pda", "主动驾驶辅助"), ("主动驾驶辅助",)),
    )

    def classify(self, question: str) -> tuple[Intent, ...]:
        normalized = question.lower()
        return tuple(
            Intent(name=name, terms=terms)
            for name, triggers, terms in self._rules
            if any(trigger in normalized for trigger in triggers)
        )

    def expand_terms(self, question: str) -> list[str]:
        terms: list[str] = []
        for intent in self.classify(question):
            terms.extend(intent.terms)
        return list(dict.fromkeys(terms))
