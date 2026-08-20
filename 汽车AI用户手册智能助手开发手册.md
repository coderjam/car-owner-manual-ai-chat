# 汽车AI用户手册智能助手开发手册 V1.0

## 1. 项目定位

本项目是一个面向车主的汽车用户手册 AI 助手系统。

系统将汽车官方用户手册转换为可检索、可问答、可溯源的知识库。用户选择车型后，可以像咨询客服一样提问，AI 根据对应车型的官方手册内容回答，并返回答案来源、页码和对应 PDF 整页图片。

## 2. V1.0 核心目标

V1.0 重点实现以下能力：

1. 用户选择车型。
2. 管理员上传或导入汽车用户手册 PDF。
3. 系统解析 PDF，生成文本知识库。
4. 系统将 PDF 每一页生成整页图片。
5. 用户在线聊天提问。
6. AI 根据对应车型知识库回答。
7. 每条回答展示来源页码和 PDF 整页图片。
8. 保存聊天历史。

## 3. 主要用户角色

### 3.1 普通用户

普通用户是车主或试用用户，主要使用 AI 问答功能。

核心操作：

- 登录系统。
- 选择车辆。
- 发起问题。
- 查看 AI 回答。
- 查看参考页码和 PDF 原页图片。
- 查看历史问答记录。

### 3.2 管理员

管理员负责维护车型和用户手册知识库。

核心操作：

- 管理车型信息。
- 上传用户手册 PDF。
- 查看文档解析状态。
- 重新生成知识库。
- 删除无效文档。
- 检查页面图片和页码映射是否正确。

## 4. 用户端功能

### 4.1 车型选择

用户首次使用时选择车辆信息。

车辆字段：

- 品牌
- 车型
- 年份
- 动力类型
- 配置版本

示例：

```text
品牌：丰田
车型：凯美瑞
年份：2026
动力：2.0 双擎
配置：运动 PLUS
```

系统根据用户选择的车型，只检索该车型绑定的用户手册知识库。

### 4.2 AI 智能问答

用户通过聊天窗口提问。

支持问题类型：

- 功能咨询：PDA 怎么开启？
- 使用咨询：双擎冬天油耗增加正常吗？
- 故障咨询：仪表出现黄色三角是什么意思？
- 驾驶辅助咨询：高速可以使用车道保持吗？
- 保养维护咨询：长途前需要检查什么？

AI 回答必须基于检索到的官方手册内容。

### 4.3 多轮连续对话

系统支持上下文理解。

每次新对话生成独立 `conversationId`。检索和大模型上下文只能读取同一用户、同一车型、同一 `conversationId` 下的最近记录，避免不同聊天之间互相污染。

示例：

```text
用户：凯美瑞 PDA 是什么？
AI：PDA 是主动驾驶辅助……

用户：高速可以使用吗？
AI：结合前文，继续说明 PDA 在高速场景下的适用条件和注意事项。
```

### 4.4 答案来源引用

检索到明确依据时，AI 回答必须返回参考来源；没有明确命中时返回空引用，并直接说明未找到依据，禁止补上一页无关内容。

来源信息包括：

- 文档名称
- 章节名称
- 手册印刷页码
- PDF 文件页码
- 引用原文片段
- PDF 整页图片地址
- PDF 原文件跳转地址

前端展示示例：

```text
参考来源
《2026 凯美瑞用户手册》
章节：驾驶辅助系统
手册页码：第 215 页
```

### 4.5 PDF 整页图片展示

用户提问后，回答下方展示命中的 PDF 页面图片。

展示规则：

- 图片展示完整 PDF 页面。
- 不裁剪。
- 不只展示局部截图。
- 不在 V1.0 做高亮框选。
- 保持原始页面比例。
- 支持点击放大预览。
- 支持左右切换多个引用页。
- 支持打开 PDF 原页。

推荐交互：

1. 聊天回答下方显示来源卡片。
2. 来源卡片中显示页码、章节、原文片段。
3. 来源卡片中显示 PDF 整页缩略图。
4. 点击缩略图进入大图预览。
5. 大图预览支持缩放。

### 4.6 聊天历史

系统保存每次问答记录。

保存内容：

- 用户 ID
- 车型 ID
- 对话 ID
- 用户问题
- AI 回答
- 引用来源
- 创建时间

## 5. 后台管理功能

### 5.1 用户手册上传

管理员上传用户手册。

支持格式：

- PDF
- 扫描版 PDF

上传时填写：

- 文件名称
- 品牌
- 车型
- 年份
- 动力类型
- 配置版本
- 手册来源
- 来源 URL

### 5.2 文档解析流程

文档处理流程：

```text
PDF 上传
↓
保存原始 PDF
↓
提取 PDF 文本
↓
扫描页 OCR 识别
↓
文本清洗
↓
识别章节
↓
按页切分内容
↓
生成知识片段
↓
生成向量
↓
保存到向量数据库
↓
生成 PDF 整页图片
↓
更新处理状态
```

### 5.3 PDF 页面图片生成

上传手册后，系统提前将 PDF 每一页渲染成图片。

推荐方案：

- 图片格式：WebP
- 图片宽度：1200 到 1600 像素
- 图片比例：保持 PDF 原始页面比例
- 存储路径：按文档 ID 和页码分目录保存

示例路径：

```text
/manuals/{documentId}/pages/{pdfPageNumber}.webp
```

这样用户提问后，系统可以直接返回图片地址，不需要临时截图。

### 5.4 知识库管理

管理员可以：

- 查看手册列表。
- 查看解析状态。
- 查看总页数。
- 查看已生成页面图片数量。
- 查看知识片段数量。
- 删除文档。
- 重新解析文档。
- 重新生成向量。
- 重新生成页面图片。

## 6. 页码设计

汽车手册常见问题是 PDF 文件页码和手册印刷页码不一致。

例如：

```text
PDF 第 229 页 = 手册印刷第 215 页
```

原因可能是 PDF 前面有封面、版权页、目录页。

因此系统需要同时保存两个页码：

- `pdf_page_number`：PDF 文件真实页序号，用于打开 PDF 和显示整页图片。
- `printed_page_number`：手册印刷页码，用于展示给用户。

前端优先显示手册印刷页码。

如果无法识别印刷页码，则显示 PDF 页码。

## 7. RAG 问答流程

用户提问示例：

```text
凯美瑞 PDA 怎么开启？
```

系统处理流程：

```text
用户提问
↓
读取用户当前车型
↓
限定车型知识库范围
↓
对问题生成向量
↓
向量检索相关知识片段
↓
按相关度选取 Top K 内容
↓
组织 Prompt
↓
调用大模型
↓
生成答案
↓
返回答案和引用来源
↓
前端展示页码和 PDF 整页图片
```

Prompt 要求：

```text
你是汽车用户手册智能助手。
请只根据提供的官方用户手册资料回答。
如果资料中没有明确答案，请说明手册中未找到明确说明。
回答应简洁、准确，并提醒用户遵守车辆实际提示和安全要求。
```

## 8. 回答结果数据格式

AI 问答接口推荐返回结构：

```json
{
  "answer": "PDA 可通过车辆设置中的驾驶辅助相关菜单开启。具体入口可能因配置不同略有差异，请以车辆中控屏实际显示为准。",
  "references": [
    {
      "documentId": 12,
      "documentName": "2026 凯美瑞用户手册",
      "chapter": "驾驶辅助系统",
      "pdfPageNumber": 229,
      "printedPageNumber": 215,
      "quote": "PDA 主动驾驶辅助相关说明……",
      "pageImageUrl": "/manuals/12/pages/229.webp",
      "pdfPageUrl": "/manuals/12/file#page=229"
    }
  ]
}
```

前端显示：

```text
参考来源：《2026 凯美瑞用户手册》
章节：驾驶辅助系统
手册页码：第 215 页
```

前端使用 `pageImageUrl` 展示整页图片。

系统使用 `pdfPageNumber` 定位 PDF 原页。

## 9. 数据库核心表

### 9.1 用户表 tb_user

```sql
CREATE TABLE tb_user (
    id BIGSERIAL PRIMARY KEY,
    username VARCHAR(100) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    create_time TIMESTAMP NOT NULL DEFAULT NOW()
);
```

### 9.2 车辆表 tb_vehicle

```sql
CREATE TABLE tb_vehicle (
    id BIGSERIAL PRIMARY KEY,
    brand VARCHAR(100) NOT NULL,
    model VARCHAR(100) NOT NULL,
    year INT NOT NULL,
    engine VARCHAR(100),
    configuration VARCHAR(100),
    create_time TIMESTAMP NOT NULL DEFAULT NOW()
);
```

### 9.3 用户车辆表 tb_user_vehicle

```sql
CREATE TABLE tb_user_vehicle (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES tb_user(id),
    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id),
    buy_date DATE,
    mileage INT,
    create_time TIMESTAMP NOT NULL DEFAULT NOW()
);
```

### 9.4 手册文件表 tb_manual

```sql
CREATE TABLE tb_manual (
    id BIGSERIAL PRIMARY KEY,
    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id),
    file_name VARCHAR(255) NOT NULL,
    file_path VARCHAR(500) NOT NULL,
    source_type VARCHAR(50),
    source_url VARCHAR(500),
    status VARCHAR(50) NOT NULL DEFAULT 'uploaded',
    total_pages INT DEFAULT 0,
    create_time TIMESTAMP NOT NULL DEFAULT NOW()
);
```

### 9.5 手册页面表 tb_manual_page

```sql
CREATE TABLE tb_manual_page (
    id BIGSERIAL PRIMARY KEY,
    manual_id BIGINT NOT NULL REFERENCES tb_manual(id),
    pdf_page_number INT NOT NULL,
    printed_page_number INT,
    chapter VARCHAR(255),
    page_text TEXT,
    page_image_url VARCHAR(500),
    create_time TIMESTAMP NOT NULL DEFAULT NOW(),
    UNIQUE (manual_id, pdf_page_number)
);
```

### 9.6 知识片段表 tb_knowledge_chunk

```sql
CREATE TABLE tb_knowledge_chunk (
    id BIGSERIAL PRIMARY KEY,
    manual_id BIGINT NOT NULL REFERENCES tb_manual(id),
    manual_page_id BIGINT NOT NULL REFERENCES tb_manual_page(id),
    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id),
    chapter VARCHAR(255),
    content TEXT NOT NULL,
    chunk_index INT NOT NULL,
    embedding VECTOR(1536),
    create_time TIMESTAMP NOT NULL DEFAULT NOW()
);
```

### 9.7 聊天记录表 tb_chat_history

```sql
CREATE TABLE tb_chat_history (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES tb_user(id),
    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id),
    conversation_id VARCHAR(64),
    question TEXT NOT NULL,
    answer TEXT NOT NULL,
    references_json JSONB,
    create_time TIMESTAMP NOT NULL DEFAULT NOW()
);
```

## 10. 接口设计

### 10.1 车型列表

```http
GET /api/vehicles
```

返回：

```json
[
  {
    "id": 1,
    "brand": "丰田",
    "model": "凯美瑞",
    "year": 2026,
    "engine": "2.0 双擎",
    "configuration": "运动 PLUS"
  }
]
```

### 10.2 用户选择车型

```http
POST /api/user-vehicles
```

请求：

```json
{
  "vehicleId": 1,
  "buyDate": "2026-01-10",
  "mileage": 3000
}
```

### 10.3 AI 问答

```http
POST /api/chat
```

请求：

```json
{
  "userId": 1,
  "vehicleId": 1,
  "question": "凯美瑞 PDA 怎么开启？",
  "conversationId": "8eeb4ff4-b0ec-4ed7-9c13-b7aa267cd186"
}
```

返回：

```json
{
  "answer": "PDA 可通过车辆设置中的驾驶辅助相关菜单开启……",
  "references": [
    {
      "documentId": 12,
      "documentName": "2026 凯美瑞用户手册",
      "chapter": "驾驶辅助系统",
      "pdfPageNumber": 229,
      "printedPageNumber": 215,
      "quote": "PDA 主动驾驶辅助相关说明……",
      "pageImageUrl": "/manuals/12/pages/229.webp",
      "pdfPageUrl": "/manuals/12/file#page=229"
    }
  ]
}
```

### 10.4 上传用户手册

```http
POST /api/admin/manuals
Content-Type: multipart/form-data
```

表单字段：

- `vehicleId`
- `file`
- `sourceType`
- `sourceUrl`

### 10.5 查看手册处理状态

```http
GET /api/admin/manuals/{manualId}
```

返回：

```json
{
  "id": 12,
  "fileName": "2026 凯美瑞用户手册.pdf",
  "status": "completed",
  "totalPages": 504,
  "generatedPageImages": 504,
  "knowledgeChunks": 1880
}
```

## 11. 前端页面

### 11.1 登录页

用于用户登录系统。

### 11.2 车型选择页

用于用户选择自己的车辆。

### 11.3 AI 聊天页

核心页面。

页面区域：

- 左侧：聊天历史。
- 中间：当前问答消息。
- 底部：问题输入框。
- 回答下方：参考来源和 PDF 整页图片。

### 11.4 PDF 页面预览

点击来源图片后打开。

能力：

- 查看整页图片。
- 放大缩小。
- 切换上一页和下一页引用。
- 打开 PDF 原文件对应页。

### 11.5 后台文档管理页

管理员使用。

页面能力：

- 上传手册。
- 查看解析状态。
- 查看页面图片生成状态。
- 重新生成知识库。
- 删除手册。

## 12. 技术架构

前端：

- Vue 3
- TypeScript
- Vite
- Element Plus

后端：

- ASP.NET Core 10 / Minimal API（当前可运行版本；业务代码未使用 .NET 10 专属特性）
- REST API
- 用户、车型、聊天、文档管理

AI 服务：

- Python FastAPI
- PDF 文本提取
- OCR
- 文档切片
- Embedding
- RAG 检索
- Prompt 管理

数据库：

- PostgreSQL
- pgvector

大模型：

- 通义千问 API
- DeepSeek API 备用
- GPT API 可作为备用扩展

OCR：

- PaddleOCR

PDF 页面渲染：

- Poppler
- PyMuPDF

## 13. 推荐项目目录

```text
car-owner-manual-ai-chat/
├── docs/
│   └── development-manual.md
├── frontend/
│   ├── src/
│   │   ├── api/
│   │   ├── components/
│   │   ├── pages/
│   │   ├── router/
│   │   └── stores/
│   └── package.json
├── backend/
│   ├── src/
│   │   ├── Controllers/
│   │   ├── Services/
│   │   ├── Models/
│   │   └── Data/
│   └── CarManualAssistant.Api.csproj
├── ai-service/
│   ├── app/
│   │   ├── api/
│   │   ├── rag/
│   │   ├── ocr/
│   │   ├── pdf/
│   │   └── prompts/
│   └── requirements.txt
├── storage/
│   ├── manuals/
│   └── page-images/
├── docker-compose.yml
└── README.md
```

## 14. V1.0 不做功能

以下功能放到 V2：

- 图片识别故障灯。
- AI 看车辆照片。
- 保养提醒。
- 油耗分析。
- 维修诊断。
- 车辆档案深度管理。
- 多品牌大规模车型库。
- PDF 页面局部高亮框选。

## 15. 开发任务拆分

### 第一阶段：基础框架

- 创建前端项目。
- 创建 ASP.NET Core API 项目。
- 创建 Python FastAPI AI 服务。
- 配置 PostgreSQL 和 pgvector。
- 建立基础数据库表。

### 第二阶段：车型和用户

- 实现登录。
- 实现车型管理。
- 实现用户选择车型。

### 第三阶段：手册上传和解析

- 实现 PDF 上传。
- 保存原始 PDF。
- 提取文本。
- 扫描件 OCR。
- 按页保存内容。
- 生成 PDF 整页图片。

### 第四阶段：知识库

- 文本清洗。
- 章节识别。
- 知识片段切分。
- 生成 Embedding。
- 写入 pgvector。

### 第五阶段：AI 问答

- 实现问题向量化。
- 实现车型范围检索。
- 实现 Prompt 拼接。
- 调用大模型。
- 返回答案和引用来源。

### 第六阶段：前端聊天体验

- 聊天窗口。
- 多轮上下文。
- 来源卡片。
- PDF 整页图片展示。
- 图片放大预览。
- 聊天历史。

### 第七阶段：后台管理

- 文档列表。
- 状态展示。
- 重新解析。
- 删除文档。
- 查看页面图片。

### 第八阶段：部署

- Docker Compose。
- 环境变量配置。
- 文件存储目录挂载。
- 数据库初始化。

## 16. 关键原则

1. AI 回答必须基于官方用户手册。
2. 有明确手册依据的答案必须带来源；无依据时必须返回空引用。
3. 来源必须能追溯到具体页码。
4. 页码和 PDF 整页图片必须对应同一页。
5. PDF 图片展示整页，不做局部裁剪。
6. 车型知识库必须隔离，避免不同车型内容混用。
7. 用户看见的是手册页码，系统内部用 PDF 页码定位文件。
8. 如果手册资料中没有明确答案，AI 应明确说明未找到依据。
