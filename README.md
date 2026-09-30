# WebCreatorDemo

一个基于 Blazor Server 的 AI 网页生成演示应用。用户可通过对话描述需求，生成并预览 HTML 页面；登录后可保存页面、恢复会话，并查看聊天历史。

## 功能说明 （任务拆分以及实现的优先顺序）

- **AI 网页生成**：在首页与项目助手对话。AI 工作流生成两个 HTML 候选方案、评估质量并选出一个结果。(单个html文件即可包含简单演示功能，易于实现)
- **HTML 预览与下载**：在首页内预览生成的 HTML，也可以下载为 `.html` 文件。
- **身份认证**：使用 Microsoft Identity Web 和 OpenID Connect。Projects 与 Chat history 的导航入口仅对已登录用户显示。
- **保存网页**：登录后可将生成的网页保存到 Azure Table Storage，并在 Projects 页面查看和打开。
- **会话持久化**：聊天历史和生成的 HTML 按当前用户及 `sessionid` 分别保存。首页 URL 中的 `sessionid` 用于恢复对应会话；新会话会生成新的 ID。
- **聊天历史**：Chat history 页面按最近更新时间列出当前用户的会话，并显示首条用户消息；Load 操作会回到首页并加载所选会话。

## 页面路由

| 页面 | 路由 | 用途 |
| --- | --- | --- |
| Home | `/` | 对话、生成 HTML、预览、下载和保存网页；支持 `page` 与 `sessionid` 查询参数 |
| Projects | `/projects` | 查看当前用户保存的网页 |
| Chat history | `/chathistory` | 查看当前用户保存的聊天会话并加载会话 |

## 项目架构

```text
Components/
  App.razor                 HTML 文档入口、静态资源和 Blazor 脚本
  Routes.razor              页面路由和默认布局
  Layout/
    MainLayout.razor         应用主布局、登录状态和导航容器
    NavMenu.razor            首页、Projects、Chat history 导航
  Pages/
    Home.razor               聊天工作区、会话恢复和 HTML 预览
    Projects.razor           用户保存的 HTML 页面
    ChatHistory.razor        用户聊天历史列表
    Error.razor              错误页面
    NotFound.razor           未找到页面

Services/
  WebCreateChatHandler.cs    Azure OpenAI 客户端、AI Agent 会话及聊天调用
  WebPageGeneratorAgent.cs   Agent 配置和系统提示词加载
  HtmlGenerationTool.cs      生成、评估并选择 HTML 候选方案的 AI 工具
  UserStateService.cs        用户会话状态读写、聊天历史 JSON 序列化
  HtmlPageStorageService.cs  Azure Table Storage 数据访问

models/
  SavedHtmlEntity.cs         用户保存的网页实体
  UserChatHistoryEntity.cs   用户聊天历史实体
  UserGeneratedHtmlEntity.cs 用户生成 HTML 实体

Resources/
  createhtmlpromt.txt        AI Agent 系统提示词

wwwroot/
  app.css                   全局样式
  html-host/                默认 HTML 预览页面
  js/download.js            HTML 文件下载脚本
  lib/bootstrap/            Bootstrap 静态资源
```

### 主要调用关系

1. `Home.razor` 接收用户输入，并调用作用域内的 `WebCreateChatHandler`。
2. `WebCreateChatHandler` 使用 `WebPageGeneratorAgent` 创建的 Agent 调用 Azure OpenAI；Agent 可调用 `HtmlGenerationTool` 生成并选择 HTML。
3. Home 将对话与生成结果交给 `UserStateService`；该服务通过 `HtmlPageStorageService` 持久化数据。
4. `Projects.razor` 查询用户保存的页面；`ChatHistory.razor` 查询该用户的会话记录。
5. 会话数据以用户 ID 作为 Azure Table `PartitionKey`，以 session ID 的 SHA-256 十六进制摘要作为 `RowKey`；实体另存原始 `SessionId`。

## 技术栈

- .NET 10 / ASP.NET Core
- Blazor Interactive Server
- Microsoft Identity Web / OpenID Connect
- Azure OpenAI、Microsoft Agents AI、Microsoft.Extensions.AI
- Azure.Data.Tables
- Bootstrap

## 配置

应用通过配置文件、环境变量或 .NET User Secrets 读取以下配置。不要将真实密钥或连接字符串提交到 Git。

| 配置键 | 说明 |
| --- | --- |
| `AzureAd:Instance` | Microsoft Entra ID 登录地址 |
| `AzureAd:Domain` | 租户域名 |
| `AzureAd:TenantId` | 租户 ID |
| `AzureAd:ClientId` | 应用注册的客户端 ID |
| `AzureAd:CallbackPath` | OpenID Connect 回调路径 |
| `AzureAd:SignedOutCallbackPath` | 登出回调路径 |
| `AzureStorage:ConnectionString` | Azure Storage 连接字符串 |
| `AzureStorage:TableName` | 保存网页的表名；未配置时默认为 `SavedHtmlPages` |
| `AzureFoundry:Endpoint` | Azure OpenAI 端点 |
| `AzureFoundry:ApiKey` | Azure OpenAI API 密钥 |
| `AzureFoundry:Model` | 部署的模型名称；代码默认值为 `gpt-5-mini` |

聊天记录和生成 HTML 使用固定表名 `usersessions`、`usergeneratedhtml`；保存的网页使用 `AzureStorage:TableName` 指定的表。

## 本地运行

前置条件：安装 .NET 10 SDK，并配置上述身份认证、Azure Storage 和 Azure OpenAI 设置。
Azure Storage 需配置本地模拟Azurite emulato(https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite)；
LLM 模型需部署于AzureFoundry。

```powershell
dotnet restore
dotnet run --launch-profile https
```

默认 HTTPS 地址：<https://localhost:7254>。HTTP 地址：<http://localhost:5272>。

## Build

```powershell
dotnet build BlazorWebCreateAgent.csproj --configuration Release
```
## 部署

部署于https://webappwebcreatordemo-cjhze3cdcgdffvf3.westus3-01.azurewebsites.net/
云端资源部署在Azure Portal, 需要storage account, App service, Foundry以及app registration.

## 一些不足
1. 未能输出AI思考的过程，在等待AI完成任务的数分钟内没有输出内容；
2. 保存html不能起中文名，且缺乏提示；
3. 简单起见，有些提示直接显示在对话框里，不够明显；
4. 项目应当包含更多上下文，以及增删改查等。作为演示仅保存html文档本身；

## 进一步深化设计
1. 在生成过程中产生更多交互，引导用户明确需求后再生成相应代码；
2. 生成后可进一步交互并修正设计；
3. 支持更复杂的web架构
4. 开发 AITool可通过Bicep一键创建云端资源，实现生成即部署避免繁琐的云端设置；

## 测试用户
testuser1@sunyiping88gmail.onmicrosoft.com
Puvu3746751

testuser2@sunyiping88gmail.onmicrosoft.com (Contains major test cases )
Qula7138151

testuser3@sunyiping88gmail.onmicrosoft.com (empty history)
Noyu3560681

## 测试样例
登录 testuser2，
1. Projects/kanban2.html, KANBAN.html, 大部分正常JS功能正常工作；
2. Projects/signouttest.html, inpagenavigationtest.html, 这些例子都不能破坏sandbox；
3. ChatHistory/hello your name is Bob, 给出上下文，AI可以记住；
4. ChatHistory/your name is Bob, 重新载入上下文， AI仍然可以记得之前的对话；
5. ChatHistory/what's your name\， 开辟新对话以及切换对话，历史不会污染；