export interface Vehicle {
  id: number;
  brand: string;
  model: string;
  year: number;
  engine: string;
  configuration: string;
}

export interface KnowledgeReference {
  documentId: number;
  documentName: string;
  chapter: string;
  pdfPageNumber: number;
  printedPageNumber?: number | null;
  quote: string;
  pageImageUrl: string;
  pdfPageUrl: string;
  totalPages?: number;
}

export interface ChatResponse {
  answer: string;
  references: KnowledgeReference[];
  chatHistoryId: number;
  createTime: string;
}

export interface ChatHistory {
  id: number;
  userId: number;
  vehicleId: number;
  conversationId: string;
  question: string;
  answer: string;
  references: KnowledgeReference[];
  createTime: string;
}

export interface Manual {
  id: number;
  vehicleId: number;
  fileName: string;
  pdfUrl?: string;
  sourceType?: string;
  sourceUrl?: string;
  status: string;
  totalPages: number;
  generatedPageImages: number;
  knowledgeChunks: number;
  createTime: string;
}

export interface UserManual {
  id: number;
  fileName: string;
  pdfUrl: string;
  totalPages: number;
}

export interface ManualManifestPage {
  pdfPageNumber: number;
  printedPageNumber?: number | null;
  chapter: string;
  pageText: string;
  pageImageUrl: string;
}

export interface ManualManifest {
  totalPages: number;
  pages: ManualManifestPage[];
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  references?: KnowledgeReference[];
  pending?: boolean;
  error?: boolean;
  retryQuestion?: string;
}
