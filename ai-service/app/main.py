from fastapi import FastAPI, HTTPException

from app.models import ChatRequest, ChatResponse, ProcessManualRequest, ProcessManualResult
from app.services.manual_processor import ManualProcessor
from app.services.rag_engine import RagEngine

app = FastAPI(
    title="汽车AI用户手册助手 AI 服务",
    version="1.0.0",
)

rag_engine = RagEngine()
manual_processor = ManualProcessor()


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok", "service": "ai-service"}


@app.post("/rag/chat", response_model=ChatResponse)
def chat(request: ChatRequest) -> ChatResponse:
    return rag_engine.answer(request)


@app.post("/manuals/process", response_model=ProcessManualResult)
def process_manual(request: ProcessManualRequest) -> ProcessManualResult:
    try:
        result = manual_processor.process(request)
        rag_engine.index_pages(
            manual_id=request.manualId,
            vehicle_id=request.vehicleId,
            document_name=request.documentName or "用户手册.pdf",
            pages=result.pages,
        )
        # 解析清单代表一份可恢复的完整知识库，因此在检索索引成功写入后
        # 再原子落盘，避免重启时把半完成任务误判成 completed。
        manual_processor.save_manifest(result, request.pageImageDirectory)
        return result
    except FileNotFoundError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"文档解析失败：{exc}") from exc
