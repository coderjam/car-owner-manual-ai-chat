# 汽车AI用户手册智能助手

这是一个汽车用户手册 AI 问答系统 V1 骨架，围绕“用户提问后返回答案、页码和对应 PDF 整页图片”来设计。

当前项目包含：

- Vue 3 前端工作台
- 独立用户端聊天页
- 独立管理后台和密码登录
- ASP.NET Core 后端 API
- Python FastAPI AI 服务
- PostgreSQL + pgvector 数据库脚本
- Docker Compose 部署配置
- 已初始化的 2026 凯美瑞智能电混双擎官方用户手册

## 技术栈

前端：

- Vue 3
- TypeScript
- Vite
- Element Plus
- Axios
- lucide-vue-next

后端：

- ASP.NET Core
- Minimal API
- HttpClient
- 静态文件服务
- 内存仓储
- PostgreSQL 持久化仓储

AI 服务：

- Python FastAPI
- PyMuPDF
- Pillow
- 轻量 RAG 检索骨架

数据库：

- PostgreSQL
- pgvector

部署：

- Docker Compose
- Nginx

## 目录结构

```text
.
├── backend/                       # ASP.NET Core 后端
├── frontend/                      # Vue 3 前端
├── ai-service/                    # Python FastAPI AI 服务
├── database/                      # PostgreSQL + pgvector SQL
├── storage/                       # PDF 和 PDF 整页图片
├── docker-compose.yml
├── 汽车AI用户手册智能助手开发手册.md
└── README.md
```

## 本地启动

默认使用内存业务仓储，适合直接体验页面和问答主流程。首份正式手册会从磁盘解析清单恢复；用户、车辆绑定和聊天记录仍建议通过 PostgreSQL 持久化。

切换 PostgreSQL：

```bash
export Database__Provider=Postgres
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=car_manual_ai;Username=car_manual;Password=car_manual_dev"
```

启动 AI 服务：

```bash
cd ai-service
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app.main:app --host 0.0.0.0 --port 8001
```

启动后端：

```bash
cd backend
dotnet run
```

后端默认地址：

```text
http://localhost:5080
```

启动前端：

```bash
cd frontend
npm install
npm run dev
```

前端默认地址：

```text
http://localhost:5173
```

## 当前初始化数据

车型：

```text
2026 丰田凯美瑞 · 智能电混双擎 · 通用版
```

手册数据位于：

```text
storage/manuals/1/
├── original.pdf       # 90 MB 原始官方手册
├── manifest.json      # 504 页文字、章节、页码和图片映射
└── pages/             # 504 张 WebP 整页图片
```

AI 检索索引位于 `ai-service/data/index.json`。当前手册共 504 页，生成 504 张整页图片和 539 个知识片段；项目重启后会从 `manifest.json` 恢复手册状态，不需要重复渲染。

## Docker 启动

```bash
docker compose up --build
```

服务地址：

- 前端：http://localhost:5173
- 用户端：http://localhost:5173/
- 管理后台：http://localhost:5173/admin
- 后端：http://localhost:5080
- AI 服务：http://localhost:8001
- PostgreSQL：localhost:5432

默认后台账号：

```text
账号：admin
密码：admin123
```

后台登录状态保存在浏览器会话中，关闭浏览器后需要重新登录。

## 已实现功能

用户端：

- 演示登录
- 车型选择
- 一键快捷提问和自由输入
- 按独立会话隔离的多轮上下文
- 按完整会话恢复聊天历史
- 回答复制、失败重试和数据加载状态
- 答案来源展示
- PDF 整页图片缩略图
- PDF 原页放大预览和多引用切换
- 整页图片加载失败提示

后台：

- 独立后台入口
- 管理员密码登录
- 后台接口令牌保护
- 手册 PDF 上传接口
- 上传文件扩展名、Content-Type 和 PDF 文件头校验
- 默认上传大小限制 200 MB
- 手册列表接口
- 手册处理状态接口
- 异步重新解析和自动状态刷新
- 删除手册接口
- 上传后触发 AI 服务解析

AI 服务：

- PDF 文本提取
- PDF 整页图片生成 WebP
- 按页保存文本和图片地址
- 意图分类和汽车领域同义词扩展
- 相关性阈值过滤，低相关结果不展示 PDF 引用
- 可选 sentence-transformers Embedding 检索，未安装模型时自动回退关键词检索
- AI 依据审查，资料不足时清空引用并明确告知用户
- 无明确命中时不返回无关页面
- 使用当前会话历史理解连续追问
- 可选接入 OpenAI-compatible 大模型接口
- 返回答案和 references

数据库：

- 用户表
- 车辆表
- 用户车辆表
- 手册表
- 手册页面表
- 知识片段表
- 聊天历史表
- 对话标识和会话内时间索引
- pgvector 向量索引

## 回答引用格式

聊天接口返回：

```json
{
  "answer": "根据《2026 凯美瑞用户手册》驾驶辅助系统第 215 页……",
  "references": [
    {
      "documentId": 1,
      "documentName": "2026 凯美瑞用户手册.pdf",
      "chapter": "驾驶辅助系统",
      "pdfPageNumber": 229,
      "printedPageNumber": 215,
      "quote": "PDA 主动驾驶辅助用于……",
      "pageImageUrl": "/manuals/1/pages/229.webp",
      "pdfPageUrl": "/manuals/1/original.pdf#page=229",
      "totalPages": 504
    }
  ]
}
```

前端使用 `pageImageUrl` 展示整页图片，使用 `printedPageNumber` 展示手册页码，使用 `pdfPageNumber` 定位 PDF 文件页。
当前初始化手册同时提供真实整页图片和原始 PDF，引用页可以直接放大或按页打开 PDF。

## 后续开发重点

当前后端已经支持两种数据模式：

- `Memory`：默认本地模式，不需要数据库；初始手册从磁盘清单恢复，聊天记录仅在当前进程保存。
- `Postgres`：持久化模式，保存用户、车型、手册、页面和聊天历史。

后续还需要：

- 接入 PaddleOCR
- 增加管理员权限
- 增加文档解析任务队列
- 增加 PDF 印刷页码校准工具

## 大模型配置

AI 服务支持 OpenAI-compatible Chat Completions 接口。

本地接入 DeepSeek：复制根目录 `.env.example` 为 `.env`，填写 API Key：

```bash
LLM_API_KEY=你的DeepSeek_API_Key
LLM_BASE_URL=https://api.deepseek.com
LLM_MODEL=deepseek-v4-flash
LLM_THINKING=disabled
LLM_EVIDENCE_JUDGE=true
RAG_EMBEDDING_ENABLED=false
EMBEDDING_MODEL=BAAI/bge-small-zh-v1.5
```

DeepSeek 使用 OpenAI 兼容的 `/chat/completions` 接口，服务会自动补全接口路径。`LLM_THINKING=disabled` 更适合需要快速返回页码和原文依据的手册问答。

也可以手动导出环境变量后启动 AI 服务：

```bash
export LLM_API_KEY=你的DeepSeek_API_Key
export LLM_BASE_URL=https://api.deepseek.com
export LLM_MODEL=deepseek-v4-flash
export LLM_THINKING=disabled
```

不配置 `LLM_API_KEY` 时，系统会使用本地规则回答，仍然返回页码和 PDF 整页图片。

### 启用 Embedding 检索

AI 服务已经预留 `sentence-transformers` 检索器。安装依赖后，将
`RAG_EMBEDDING_ENABLED=true`，服务会加载 `EMBEDDING_MODEL` 并把语义相似度与
关键词得分融合；模型不可用时自动回退，不影响基础问答。
