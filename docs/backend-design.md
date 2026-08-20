# 后端设计说明

## 1. 后端职责

后端负责系统的业务接口和数据一致性，主要职责包括：

- 用户登录入口。
- 管理员密码登录入口。
- 车型查询和用户车型选择。
- AI 问答接口。
- 聊天历史保存。
- 用户手册上传。
- 用户手册解析任务触发。
- PDF 和整页图片静态访问。
- 手册、页面、引用来源数据持久化。

后端不直接做 OCR、PDF 渲染和 RAG 检索。这些能力交给 Python AI 服务完成。

## 2. 服务边界

```text
Vue 前端
↓
ASP.NET Core 后端
↓
Python AI 服务
↓
PostgreSQL + pgvector
```

后端只暴露统一 API 给前端，前端不直接访问 AI 服务。

用户端和管理后台在前端入口上分开：

- 用户端：`/`
- 管理后台：`/admin`

管理后台接口也在后端权限上分开：

- 用户问答接口：`/api/chat`
- 后台登录接口：`/api/admin/auth/login`
- 后台管理接口：`/api/admin/manuals`

这样做的好处：

- 前端不用关心模型供应商。
- 后端可以统一保存聊天历史。
- 后端可以控制用户权限。
- 后端可以保证车型、手册、页码、图片地址的一致性。

## 3. 数据仓储设计

后端通过 `IAppStore` 抽象数据访问。

目前有两个实现：

- `AppStore`：内存仓储，适合本地演示。
- `PostgresAppStore`：PostgreSQL 持久化仓储，适合正式部署。

配置方式：

```json
{
  "Database": {
    "Provider": "Memory"
  }
}
```

切换 PostgreSQL：

```json
{
  "Database": {
    "Provider": "Postgres"
  },
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=car_manual_ai;Username=car_manual;Password=car_manual_dev"
  }
}
```

## 4. 为什么要保留内存仓储

V1 开发阶段保留内存仓储有三个作用：

1. 不启动数据库也能体验前端和问答主流程。
2. 前端开发可以直接拿到稳定的 demo 数据。
3. AI 服务未完全接入时，后端仍能返回示例引用页。

正式部署时使用 PostgreSQL。

## 5. 管理员登录设计

V1 管理后台使用配置文件中的账号密码。

默认配置：

```json
{
  "Admin": {
    "Username": "admin",
    "Password": "admin123",
    "Token": "dev-admin-token"
  }
}
```

登录接口：

```http
POST /api/admin/auth/login
```

登录成功后返回后台令牌。前端访问手册上传、删除、重新解析等接口时，需要携带：

```text
Authorization: Bearer dev-admin-token
```

生产环境应替换为：

- 管理员表。
- 密码哈希。
- JWT。
- 登录失败次数限制。
- 操作审计日志。

## 6. PDF 页码设计

后端同时保存两个页码：

- `pdf_page_number`
- `printed_page_number`

其中：

- `pdf_page_number` 是 PDF 文件的真实页序号，用于定位文件和页面图片。
- `printed_page_number` 是手册印刷页码，用于展示给用户。

示例：

```text
PDF 第 229 页 = 手册印刷第 215 页
```

前端显示：

```text
手册第 215 页
```

系统内部访问：

```text
/manuals/1/pages/229.webp
/manuals/1/original.pdf#page=229
```

这个设计可以避免封面、目录、版权页导致的页码偏移问题。

## 7. 问答接口流程

接口：

```http
POST /api/chat
```

流程：

```text
接收用户问题
↓
检查车型是否存在
↓
读取最近几轮聊天历史
↓
调用 Python AI 服务
↓
AI 服务返回答案和 references
↓
后端保存聊天历史
↓
返回给前端
```

如果 AI 服务不可用，后端会使用本地页面索引返回兜底引用，保证前端仍然能看到页码和整页图。

## 8. 手册上传流程

接口：

```http
POST /api/admin/manuals
```

流程：

```text
接收 PDF
↓
保存原始 PDF 到 storage/manuals/{manualId}/original.pdf
↓
写入 tb_manual
↓
触发 AI 服务解析
↓
AI 服务生成每页整页图片
↓
AI 服务返回页面列表
↓
后端写入 tb_manual_page
↓
更新手册状态
```

V1 当前使用 `Task.Run` 触发解析任务。

正式生产建议替换为：

- Hangfire
- Quartz
- RabbitMQ
- Kafka
- 云函数任务队列

## 9. 静态文件访问

后端将 `storage/manuals` 映射为：

```text
/manuals
```

因此页面图片地址可以是：

```text
/manuals/12/pages/229.webp
```

前端拿到地址后直接展示完整图片。

## 10. 当前后端重点注释位置

重点注释在以下文件：

- `backend/Program.cs`
- `backend/Services/PostgresAppStore.cs`
- `backend/Services/AiAssistantClient.cs`
- `backend/Services/ManualFileService.cs`

这些注释主要说明：

- 为什么用仓储接口。
- 为什么问答需要多轮历史。
- 为什么引用必须包含页码和整页图。
- 为什么文档解析后续应换成任务队列。
- 为什么 AI 服务不可用时仍要返回兜底引用。
