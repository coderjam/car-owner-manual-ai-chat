from pydantic import BaseModel, ConfigDict, Field


class Vehicle(BaseModel):
    model_config = ConfigDict(extra="ignore")

    id: int
    brand: str
    model: str
    year: int
    engine: str = ""
    configuration: str = ""


class KnowledgeReference(BaseModel):
    documentId: int
    documentName: str
    chapter: str
    pdfPageNumber: int
    printedPageNumber: int | None = None
    quote: str
    pageImageUrl: str
    pdfPageUrl: str
    totalPages: int = 0


class ChatHistory(BaseModel):
    model_config = ConfigDict(extra="ignore")

    id: int
    userId: int
    vehicleId: int
    conversationId: str = ""
    question: str
    answer: str
    references: list[KnowledgeReference] = Field(default_factory=list)


class ChatRequest(BaseModel):
    userId: int
    vehicleId: int
    question: str
    conversationId: str = ""
    vehicle: Vehicle | None = None
    recentHistory: list[ChatHistory] = Field(default_factory=list)


class ChatResponse(BaseModel):
    answer: str
    references: list[KnowledgeReference]
    chatHistoryId: int = 0
    createTime: str


class ManualPage(BaseModel):
    id: int = 0
    manualId: int
    pdfPageNumber: int
    printedPageNumber: int | None = None
    chapter: str = ""
    pageText: str = ""
    pageImageUrl: str = ""


class ProcessManualRequest(BaseModel):
    manualId: int
    vehicleId: int
    filePath: str
    pageImageDirectory: str
    documentName: str | None = None


class ProcessManualResult(BaseModel):
    totalPages: int
    generatedPageImages: int
    knowledgeChunks: int
    pages: list[ManualPage]
