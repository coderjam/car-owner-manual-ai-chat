from __future__ import annotations

import io
import re
from pathlib import Path

import fitz
from PIL import Image

from app.models import ManualPage, ProcessManualRequest, ProcessManualResult


class ManualProcessor:
    """负责把 PDF 变成“可检索文本 + 整页图片”。

    V1 的关键不是把文档切得多花，而是保证三件事永远对齐：
    1. 检索命中的文本片段；
    2. 这段文本所在的 PDF 页码；
    3. 前端展示给用户看的 PDF 整页图片。

    所以后续即使接入 PaddleOCR、版面分析或章节目录识别，也应该围绕 page
    这个稳定单位扩展，而不是只保存一堆脱离页码的散文本。
    """

    def process(self, request: ProcessManualRequest) -> ProcessManualResult:
        pdf_path = Path(request.filePath)
        if not pdf_path.exists():
            raise FileNotFoundError(f"PDF 文件不存在：{pdf_path}")

        image_dir = Path(request.pageImageDirectory)
        image_dir.mkdir(parents=True, exist_ok=True)

        pages: list[ManualPage] = []
        generated_images = 0
        chunk_count = 0

        with fitz.open(pdf_path) as doc:
            total_pages = doc.page_count

            for page_index in range(total_pages):
                pdf_page_number = page_index + 1
                page = doc.load_page(page_index)

                raw_text = page.get_text("text")
                printed_page_number = self._infer_printed_page_number(raw_text)
                text = self._extract_text(raw_text)
                chapter = self._infer_chapter(text)

                # 目录只用于用户翻页，不作为问答知识参与检索，避免“保养”等词
                # 总是命中目录页而不是具体操作页。
                if pdf_page_number <= 5:
                    text = ""
                page_image_url = self._render_page_image(
                    page=page,
                    manual_id=request.manualId,
                    pdf_page_number=pdf_page_number,
                    image_dir=image_dir,
                )

                if page_image_url:
                    generated_images += 1

                chunk_count += len(self._split_text(text))

                pages.append(
                    ManualPage(
                        manualId=request.manualId,
                        pdfPageNumber=pdf_page_number,
                        printedPageNumber=printed_page_number,
                        chapter=chapter,
                        pageText=text,
                        pageImageUrl=page_image_url,
                    )
                )

        result = ProcessManualResult(
            totalPages=total_pages,
            generatedPageImages=generated_images,
            knowledgeChunks=chunk_count,
            pages=pages,
        )
        return result

    def _extract_text(self, raw_text: str) -> str:
        if raw_text.strip():
            return self._clean_text(raw_text)

        # 扫描版 PDF 通常没有可提取文字。保留空文本，避免把系统提示语
        # 当成手册内容写入索引；后续在这个分支接入 OCR 即可。
        return ""

    def _render_page_image(
        self,
        page: fitz.Page,
        manual_id: int,
        pdf_page_number: int,
        image_dir: Path,
    ) -> str:
        # 宽度控制在约 1500 像素，兼顾移动端加载速度和放大后的可读性。
        page_width = max(page.rect.width, 1)
        scale = 1500 / page_width
        matrix = fitz.Matrix(scale, scale)
        pixmap = page.get_pixmap(matrix=matrix, alpha=False)

        png_bytes = pixmap.tobytes("png")
        image_path = image_dir / f"{pdf_page_number}.webp"
        with Image.open(io.BytesIO(png_bytes)) as image:
            image.save(image_path, "WEBP", quality=86)

        return f"/manuals/{manual_id}/pages/{pdf_page_number}.webp"

    def _clean_text(self, text: str) -> str:
        replacements = {
            "\uf06c": "•",
            "\uf075": "•",
            "\uf0b7": "•",
            "\uf0ae": "→",
            "\uf0be": "~",
            "\uf0b4": "×",
        }
        for source, target in replacements.items():
            text = text.replace(source, target)

        lines = [line.strip() for line in text.splitlines()]
        lines = [line for line in lines if line]

        # PDF 页码经常作为正文第一行提取出来，章节页眉也可能重复出现。
        if lines and re.fullmatch(r"\d{1,4}", lines[0]):
            lines.pop(0)

        header_lines: set[str] = set()
        cleaned_lines: list[str] = []
        for index, line in enumerate(lines):
            if index < 10 and line in header_lines:
                continue
            if index < 10:
                header_lines.add(line)
            if cleaned_lines and cleaned_lines[-1] == line:
                continue
            cleaned_lines.append(line)

        lines = cleaned_lines
        return "\n".join(lines)

    def _infer_chapter(self, text: str) -> str:
        for line in text.splitlines()[:12]:
            # 汽车手册正文页通常把“4-5. 使用驾驶辅助系统”放在页首。
            # 优先保留这个稳定的章节标题，避免退化成过于宽泛的“驾驶”。
            if re.match(r"^\d+(?:-\d+)+(?:\.\s*)?\S", line) and len(line) <= 80:
                return line

        for line in text.splitlines()[:12]:
            if any(keyword in line for keyword in ["驾驶", "安全", "空调", "多媒体", "保养", "故障", "技术参数"]):
                return line[:80]

        return "用户手册"

    def _infer_printed_page_number(self, text: str) -> int | None:
        lines = [line.strip() for line in text.splitlines() if line.strip()]
        if not lines:
            return None

        # 有些手册页码位于页眉，有些位于页脚。只检查第一个非空行和
        # 最后五行，避免把正文中的规格数字误判成页码。
        candidates = [lines[0], *reversed(lines[-5:])]
        for line in candidates:
            if re.fullmatch(r"\d{1,4}", line):
                return int(line)

        return None

    def save_manifest(
        self,
        result: ProcessManualResult,
        page_image_directory: str,
    ) -> None:
        manual_dir = Path(page_image_directory).parent
        manifest_path = manual_dir / "manifest.json"
        temporary_path = manifest_path.with_suffix(".tmp")
        temporary_path.write_text(
            result.model_dump_json(indent=2),
            encoding="utf-8",
        )
        temporary_path.replace(manifest_path)

    def _split_text(self, text: str, max_chars: int = 900) -> list[str]:
        if not text:
            return []

        chunks: list[str] = []
        current = ""

        for paragraph in text.splitlines():
            paragraph = paragraph.strip()
            if not paragraph:
                continue

            if current and len(current) + 1 + len(paragraph) > max_chars:
                chunks.append(current)
                current = ""

            while len(paragraph) > max_chars:
                chunks.append(paragraph[:max_chars])
                paragraph = paragraph[max_chars:]

            current = f"{current}\n{paragraph}".strip()

        if current:
            chunks.append(current)

        return chunks
